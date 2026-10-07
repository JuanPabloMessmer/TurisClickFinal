using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Admin.Dtos;
using TurisClick.Api.Modules.Auth.Entities;
using TurisClick.Api.Modules.Companies.Entities;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Flights.Entities;
using TurisClick.Api.Modules.Flights.Services;
using TurisClick.Api.Modules.Reservations.Entities;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Admin.Services;

public interface IAdminPlatformService
{
    Task<AdminOverviewResponse> GetOverviewAsync(CancellationToken ct);

    Task<PagedResult<AdminExperienceRowResponse>> ListExperiencesAsync(
        string? status, string? search, int page, int pageSize, CancellationToken ct);

    Task<PagedResult<AdminPackageRowResponse>> ListPackagesAsync(
        string? status, string? search, bool? withFlight, int page, int pageSize, CancellationToken ct);

    Task<PagedResult<AdminReservationRowResponse>> ListReservationsAsync(
        string? status, bool? needsAttention, int page, int pageSize, CancellationToken ct);
}

/// <summary>
/// Visibilidad global de la plataforma para el administrador.
///
/// **No reutiliza los servicios del operador**, y es deliberado: esos están construidos alrededor de "lo mío"
/// —filtran por `CompanyId` y validan propiedad— y adaptarlos para que también vean todo significaría abrir un
/// agujero en la garantía que los hace confiables. Acá se consulta la base directamente, en modo lectura, con
/// proyecciones propias.
///
/// Lo que el administrador **no** ve: datos de pasajero. No existen en la base, y que exista un rol global no
/// es razón para empezar a guardarlos.
/// </summary>
public class AdminPlatformService(TurisClickDbContext db) : IAdminPlatformService
{
    public async Task<AdminOverviewResponse> GetOverviewAsync(CancellationToken ct)
    {
        var today = DateTimeOffset.UtcNow.Date;
        var todayStart = new DateTimeOffset(today, TimeSpan.Zero);
        var weekStart = todayStart.AddDays(-7);

        var companies = await db.Companies
            .AsNoTracking()
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, ct);

        var experiences = await db.Experiences
            .AsNoTracking()
            .GroupBy(e => e.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, ct);

        var packages = await db.Packages
            .AsNoTracking()
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, ct);

        var reservations = await db.Reservations
            .AsNoTracking()
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, ct);

        var ledger = await db.PaymentTransactions
            .AsNoTracking()
            .Where(p => p.Status == PaymentTransactionStatus.SUCCEEDED)
            .GroupBy(p => new { p.Currency, p.Type })
            .Select(g => new { g.Key.Currency, g.Key.Type, Amount = g.Sum(p => p.Amount) })
            .ToListAsync(ct);

        return new AdminOverviewResponse
        {
            CompaniesApproved = companies.GetValueOrDefault(CompanyStatus.APPROVED),
            CompaniesPendingApproval = companies.GetValueOrDefault(CompanyStatus.PENDING_APPROVAL),
            CompaniesSuspended = companies.GetValueOrDefault(CompanyStatus.SUSPENDED),

            ExperiencesPublished = experiences.GetValueOrDefault(PublicationStatus.PUBLISHED),
            ExperiencesDraft = experiences.GetValueOrDefault(PublicationStatus.DRAFT),
            ExperiencesSuspended = experiences.GetValueOrDefault(PublicationStatus.SUSPENDED),

            PackagesPublished = packages.GetValueOrDefault(PublicationStatus.PUBLISHED),
            PackagesDraft = packages.GetValueOrDefault(PublicationStatus.DRAFT),
            PackagesWithFlight = await db.Packages.CountAsync(p => p.IncludesFlight, ct),

            ReservationsToday = await db.Reservations.CountAsync(r => r.CreatedAt >= todayStart, ct),
            ReservationsLast7Days = await db.Reservations.CountAsync(r => r.CreatedAt >= weekStart, ct),
            ReservationsPendingPayment = reservations.GetValueOrDefault(ReservationStatus.PENDING_PAYMENT),
            ReservationsConfirmed = reservations.GetValueOrDefault(ReservationStatus.CONFIRMED),

            ProviderAccountsPendingFirstLogin = await db.Users
                .CountAsync(u => u.Role == UserRole.PROVIDER && u.MustChangePassword, ct),

            FlightsAwaitingReconciliation = await db.FlightBookings
                .CountAsync(b => b.Status == FlightBookingStatus.RECONCILIATION_REQUIRED
                    || b.Status == FlightBookingStatus.ORDERING, ct),

            CancellationsNeedingReview = await db.ReservationCancellations
                .CountAsync(c => c.Status == ReservationCancellationStatus.REFUND_PENDING
                    || c.Status == ReservationCancellationStatus.REQUIRES_REVIEW
                    || c.Status == ReservationCancellationStatus.ACCEPTED, ct),

            ChargedByCurrency = [.. ledger
                .Where(l => l.Type == PaymentTransactionType.CHARGE)
                .Select(l => new AdminMoneyResponse { Currency = l.Currency, Amount = l.Amount })
                .OrderBy(m => m.Currency)],

            RefundedByCurrency = [.. ledger
                .Where(l => l.Type is PaymentTransactionType.REFUND or PaymentTransactionType.VOID)
                .GroupBy(l => l.Currency)
                .Select(g => new AdminMoneyResponse { Currency = g.Key, Amount = g.Sum(l => l.Amount) })
                .OrderBy(m => m.Currency)],
        };
    }

    public async Task<PagedResult<AdminExperienceRowResponse>> ListExperiencesAsync(
        string? status, string? search, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var query = db.Experiences.AsNoTracking();

        if (TryParseStatus(status, out var publication)) query = query.Where(e => e.Status == publication);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(e => EF.Functions.ILike(e.Title, $"%{term}%")
                || EF.Functions.ILike(e.Company!.Name, $"%{term}%"));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new AdminExperienceRowResponse
            {
                Id = e.Id,
                Title = e.Title,
                CompanyId = e.CompanyId,
                CompanyName = e.Company!.Name,
                CompanyStatus = e.Company.Status.ToString(),
                DestinationName = e.Destination!.Name,
                Price = e.Price,
                Currency = e.Currency,
                Status = e.Status.ToString(),
                Categories = e.Categories.Count,
                FutureAvailabilities = e.Availabilities.Count(a => a.Date >= today),
                CreatedAt = e.CreatedAt,
            })
            .ToListAsync(ct);

        return new PagedResult<AdminExperienceRowResponse>
        {
            Items = items, Page = page, PageSize = pageSize, TotalCount = total,
        };
    }

    public async Task<PagedResult<AdminPackageRowResponse>> ListPackagesAsync(
        string? status, string? search, bool? withFlight, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var query = db.Packages.AsNoTracking();

        if (TryParseStatus(status, out var publication)) query = query.Where(p => p.Status == publication);
        if (withFlight is { } flight) query = query.Where(p => p.IncludesFlight == flight);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p => EF.Functions.ILike(p.Title, $"%{term}%")
                || EF.Functions.ILike(p.Company!.Name, $"%{term}%"));
        }

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.CompanyId,
                CompanyName = p.Company!.Name,
                CompanyStatus = p.Company.Status,
                DestinationName = p.Destination!.Name,
                p.DurationDays,
                p.Price,
                p.Currency,
                p.Status,
                p.IncludesFlight,
                FlightOrigins = p.FlightRule == null ? null : p.FlightRule.AllowedOriginIatas,
                FlightDestination = p.FlightRule == null ? null : p.FlightRule.DestinationIata,
                p.CancellationPolicy,
                Categories = p.Categories.Count,
                FutureDepartures = p.Availabilities.Count(a => a.DepartureDate >= today),
                p.CreatedAt,
            })
            .ToListAsync(ct);

        var items = rows.Select(p => new AdminPackageRowResponse
        {
            Id = p.Id,
            Title = p.Title,
            CompanyId = p.CompanyId,
            CompanyName = p.CompanyName,
            CompanyStatus = p.CompanyStatus.ToString(),
            DestinationName = p.DestinationName,
            DurationDays = p.DurationDays,
            Price = p.Price,
            Currency = p.Currency,
            Status = p.Status.ToString(),
            IncludesFlight = p.IncludesFlight,
            FlightRoute = p.FlightDestination is null
                ? null
                : $"{p.FlightOrigins?.Replace(",", "/")} → {p.FlightDestination}",
            HasCancellationPolicy = !string.IsNullOrWhiteSpace(p.CancellationPolicy),
            Categories = p.Categories,
            FutureDepartures = p.FutureDepartures,
            CreatedAt = p.CreatedAt,
        }).ToList();

        return new PagedResult<AdminPackageRowResponse>
        {
            Items = items, Page = page, PageSize = pageSize, TotalCount = total,
        };
    }

    public async Task<PagedResult<AdminReservationRowResponse>> ListReservationsAsync(
        string? status, bool? needsAttention, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Reservations.AsNoTracking();

        if (Enum.TryParse<ReservationStatus>(status, ignoreCase: true, out var reservationStatus))
            query = query.Where(r => r.Status == reservationStatus);

        // "Necesita atención" es lo único que convierte este listado en una cola de trabajo: un pasaje sin
        // resolver o una cancelación que quedó a medias.
        if (needsAttention == true)
            query = query.Where(r =>
                db.FlightBookings.Any(b => b.ReservationId == r.Id
                    && (b.Status == FlightBookingStatus.RECONCILIATION_REQUIRED || b.Status == FlightBookingStatus.ORDERING))
                || db.ReservationCancellations.Any(c => c.ReservationId == r.Id
                    && (c.Status == ReservationCancellationStatus.REFUND_PENDING
                        || c.Status == ReservationCancellationStatus.REQUIRES_REVIEW
                        || c.Status == ReservationCancellationStatus.ACCEPTED)));

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new
            {
                r.Id,
                r.Status,
                r.CreatedAt,
                r.ConfirmedAt,
                r.CancelledAt,
                TouristFirst = r.Tourist!.FirstName,
                TouristLast = r.Tourist.LastName,
                FromAssistant = r.AiItineraryId != null,
                Items = r.Items.Select(i => new
                {
                    i.ProductType,
                    Title = i.ProductType == ProductType.PACKAGE ? i.Package!.Title : i.Experience!.Title,
                    Company = i.Company!.Name,
                    i.Travelers,
                    i.Subtotal,
                    i.Currency,
                }).ToList(),
                Flight = db.FlightBookings
                    .Where(b => b.ReservationId == r.Id)
                    .Select(b => new { b.Status, b.OriginIata, b.DestinationIata })
                    .FirstOrDefault(),
                CancellationStatus = db.ReservationCancellations
                    .Where(c => c.ReservationId == r.Id)
                    .OrderByDescending(c => c.CreatedAt)
                    .Select(c => (ReservationCancellationStatus?)c.Status)
                    .FirstOrDefault(),
                Money = db.PaymentTransactions
                    .Where(p => p.ReservationId == r.Id && p.Status == PaymentTransactionStatus.SUCCEEDED)
                    .Select(p => new { p.Type, p.Amount, p.Currency })
                    .ToList(),
            })
            .ToListAsync(ct);

        var items = rows.Select(r =>
        {
            var kinds = r.Items.Select(i => i.ProductType).Distinct().ToList();

            return new AdminReservationRowResponse
            {
                Id = r.Id,
                Status = r.Status.ToString(),
                CreatedAt = r.CreatedAt,
                ConfirmedAt = r.ConfirmedAt,
                CancelledAt = r.CancelledAt,
                TouristName = $"{r.TouristFirst} {r.TouristLast}".Trim(),
                Kind = kinds.Count > 1 ? "MIXED" : kinds.FirstOrDefault().ToString(),
                FromAssistant = r.FromAssistant,
                Summary = r.Items.Count switch
                {
                    0 => "Sin servicios",
                    1 => r.Items[0].Title,
                    _ => $"{r.Items[0].Title} y {r.Items.Count - 1} más",
                },
                Companies = [.. r.Items.Select(i => i.Company).Distinct()],
                Travelers = r.Items.Sum(i => i.Travelers),
                Totals = [.. r.Items
                    .GroupBy(i => i.Currency)
                    .Select(g => new AdminMoneyResponse { Currency = g.Key, Amount = g.Sum(i => i.Subtotal) })
                    .OrderBy(m => m.Currency)],
                FlightStatus = r.Flight?.Status.ToString(),
                FlightRoute = r.Flight is null ? null : $"{r.Flight.OriginIata} → {r.Flight.DestinationIata}",
                CancellationStatus = r.CancellationStatus?.ToString(),
                Charged = [.. r.Money
                    .Where(m => m.Type == PaymentTransactionType.CHARGE)
                    .GroupBy(m => m.Currency)
                    .Select(g => new AdminMoneyResponse { Currency = g.Key, Amount = g.Sum(m => m.Amount) })
                    .OrderBy(m => m.Currency)],
                Refunded = [.. r.Money
                    .Where(m => m.Type is PaymentTransactionType.REFUND or PaymentTransactionType.VOID)
                    .GroupBy(m => m.Currency)
                    .Select(g => new AdminMoneyResponse { Currency = g.Key, Amount = g.Sum(m => m.Amount) })
                    .OrderBy(m => m.Currency)],
            };
        }).ToList();

        return new PagedResult<AdminReservationRowResponse>
        {
            Items = items, Page = page, PageSize = pageSize, TotalCount = total,
        };
    }

    private static bool TryParseStatus(string? value, out PublicationStatus status) =>
        Enum.TryParse(value, ignoreCase: true, out status);
}
