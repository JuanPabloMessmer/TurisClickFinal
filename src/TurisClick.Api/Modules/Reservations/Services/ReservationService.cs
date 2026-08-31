using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Experiences.Repositories;
using TurisClick.Api.Modules.Packages.Entities;
using TurisClick.Api.Modules.Packages.Repositories;
using TurisClick.Api.Modules.Reservations.Dtos;
using TurisClick.Api.Modules.Reservations.Entities;
using TurisClick.Api.Modules.Reservations.Payments;
using TurisClick.Api.Modules.Reservations.Repositories;
using TurisClick.Api.Shared.Exceptions;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Reservations.Services;

public class ReservationService(
    IReservationRepository reservationRepository,
    IReservationItemRepository reservationItemRepository,
    IExperienceAvailabilityRepository experienceAvailabilityRepository,
    IPackageAvailabilityRepository packageAvailabilityRepository,
    IPaymentGateway paymentGateway,
    ICurrentUserContext currentUser,
    ICompanyOwnershipGuard ownershipGuard,
    ILogger<ReservationService> logger,
    TurisClickDbContext db) : IReservationService
{
    /// <summary>Ventana del hold de cupo mientras la reserva está PENDING_PAYMENT. La liberación efectiva por expiración es UC-SYS-08 (Oleada 8) — acá solo se registra el límite.</summary>
    private static readonly TimeSpan HoldWindow = TimeSpan.FromMinutes(30);

    public async Task<ReservationResponse> CreateAsync(CreateReservationRequest request, CancellationToken ct)
    {
        // Forma ya garantizada por CreateReservationRequest.Validate: exactamente uno de los dos ids.
        var (item, tx) = request.ExperienceAvailabilityId.HasValue
            ? await BuildExperienceReservationItemAsync(request.ExperienceAvailabilityId.Value, request.Travelers, ct)
            : await BuildPackageReservationItemAsync(request.PackageAvailabilityId!.Value, request.Travelers, ct);

        await using var _ = tx;

        var now = DateTimeOffset.UtcNow;

        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            TouristId = currentUser.UserId,
            Status = ReservationStatus.PENDING_PAYMENT,
            ExpiresAt = now.Add(HoldWindow),
            CreatedAt = now
        };

        item.CreatedAt = now;
        reservation.Items.Add(item);

        await reservationRepository.AddAsync(reservation, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var created = await reservationRepository.GetByIdForReadAsync(reservation.Id, ct)
            ?? throw new InvalidOperationException("La reserva recién creada no pudo leerse.");

        return ToResponse(created);
    }

    /// <summary>UC-T-08 — reserva directa de una Experience individual. Devuelve la transacción todavía abierta (se cierra en CreateAsync tras persistir la Reservation completa).</summary>
    private async Task<(ReservationItem Item, IDbContextTransaction Tx)> BuildExperienceReservationItemAsync(
        Guid experienceAvailabilityId, int travelers, CancellationToken ct)
    {
        var availability = await experienceAvailabilityRepository.GetByIdWithExperienceAsync(experienceAvailabilityId, ct)
            ?? throw new NotFoundAppException("Disponibilidad no encontrada.");

        var experience = availability.Experience
            ?? throw new InvalidOperationException("La disponibilidad no tiene una Experience asociada.");

        // UC-T-05/precondición UC-T-08: una Experience no publicada no existe para el TOURIST.
        if (experience.Status != PublicationStatus.PUBLISHED)
            throw new NotFoundAppException("Experiencia no encontrada.");

        // UC-T-08 excepción: "slot ya no existe/expiró" → 410 (distinto de sin-cupo, que es 409).
        if (availability.Status != AvailabilitySlotStatus.OPEN)
            throw new GoneAppException("El slot de disponibilidad ya no está disponible.");

        if (availability.Date < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new GoneAppException("El slot de disponibilidad ya expiró.");

        // UC-SYS-06: UPDATE condicional atómico dentro de una transacción — ver backend-architecture.md §13.
        var tx = await db.Database.BeginTransactionAsync(ct);

        var affectedRows = await db.ExperienceAvailabilities
            .Where(a => a.Id == availability.Id && a.ReservedSlots + travelers <= a.TotalSlots)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ReservedSlots, a => a.ReservedSlots + travelers), ct);

        if (affectedRows == 0)
        {
            // Disponer una transacción sin commitear la revierte implícitamente (Npgsql/EF Core) — no
            // hace falta un RollbackAsync explícito antes.
            await tx.DisposeAsync();
            throw new ConflictAppException("No hay cupo suficiente para la cantidad de viajeros solicitada.");
        }

        // UC-SYS-02: precio y moneda siempre se leen frescos desde Experience — nunca se confía en un valor del cliente.
        var item = new ReservationItem
        {
            Id = Guid.NewGuid(),
            CompanyId = experience.CompanyId,
            ProductType = ProductType.EXPERIENCE,
            ExperienceId = experience.Id,
            ExperienceAvailabilityId = availability.Id,
            Travelers = travelers,
            UnitPrice = experience.Price,
            Currency = experience.Currency,
            Subtotal = experience.Price * travelers,
            Status = ReservationItemStatus.PENDING_PAYMENT
        };

        return (item, tx);
    }

    /// <summary>UC-T-09 — reserva directa de un Package de proveedor. Mismo patrón que la Experience (UC-SYS-01/02/06), sobre PackageAvailability.</summary>
    private async Task<(ReservationItem Item, IDbContextTransaction Tx)> BuildPackageReservationItemAsync(
        Guid packageAvailabilityId, int travelers, CancellationToken ct)
    {
        var availability = await packageAvailabilityRepository.GetByIdWithPackageAsync(packageAvailabilityId, ct)
            ?? throw new NotFoundAppException("Disponibilidad no encontrada.");

        var package = availability.Package
            ?? throw new InvalidOperationException("La disponibilidad no tiene un Package asociado.");

        // UC-T-07/precondición UC-T-09: un Package no publicado no existe para el TOURIST.
        if (package.Status != PublicationStatus.PUBLISHED)
            throw new NotFoundAppException("Paquete no encontrado.");

        if (availability.Status != AvailabilitySlotStatus.OPEN)
            throw new GoneAppException("La salida ya no está disponible.");

        if (availability.DepartureDate < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new GoneAppException("La salida ya expiró.");

        var tx = await db.Database.BeginTransactionAsync(ct);

        var affectedRows = await db.PackageAvailabilities
            .Where(a => a.Id == availability.Id && a.ReservedSlots + travelers <= a.TotalSlots)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ReservedSlots, a => a.ReservedSlots + travelers), ct);

        if (affectedRows == 0)
        {
            // Disponer una transacción sin commitear la revierte implícitamente (Npgsql/EF Core) — no
            // hace falta un RollbackAsync explícito antes.
            await tx.DisposeAsync();
            throw new ConflictAppException("No hay cupo suficiente para la cantidad de viajeros solicitada.");
        }

        var item = new ReservationItem
        {
            Id = Guid.NewGuid(),
            CompanyId = package.CompanyId,
            ProductType = ProductType.PACKAGE,
            PackageId = package.Id,
            PackageAvailabilityId = availability.Id,
            Travelers = travelers,
            UnitPrice = package.Price,
            Currency = package.Currency,
            Subtotal = package.Price * travelers,
            Status = ReservationItemStatus.PENDING_PAYMENT
        };

        return (item, tx);
    }

    public async Task<ReservationResponse> GetByIdForTouristAsync(Guid id, CancellationToken ct)
    {
        var reservation = await reservationRepository.GetByIdForReadAsync(id, ct)
            ?? throw new NotFoundAppException("Reserva no encontrada.");

        if (reservation.TouristId != currentUser.UserId)
            throw new ForbiddenAppException("Esta reserva no te pertenece.");

        return ToResponse(reservation);
    }

    public async Task<PagedResult<ReservationResponse>> ListMineAsync(int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, totalCount) = await reservationRepository.ListByTouristAsync(currentUser.UserId, page, pageSize, ct);

        return new PagedResult<ReservationResponse>
        {
            Items = items.Select(r => ToResponse(r)).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<PagedResult<ReservationItemResponse>> ListReceivedByCompanyAsync(int page, int pageSize, CancellationToken ct)
    {
        var companyId = currentUser.CompanyId
            ?? throw new ForbiddenAppException("El usuario autenticado no tiene una empresa asociada.");

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, totalCount) = await reservationItemRepository.ListByCompanyAsync(companyId, page, pageSize, ct);

        return new PagedResult<ReservationItemResponse>
        {
            Items = items.Select(i => ToItemResponse(i)).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<ReservationItemResponse> GetReceivedItemByIdAsync(Guid itemId, CancellationToken ct)
    {
        var item = await reservationItemRepository.GetByIdAsync(itemId, ct)
            ?? throw new NotFoundAppException("Reserva no encontrada.");

        ownershipGuard.EnsureOwns(item.CompanyId);

        return ToItemResponse(item);
    }

    public async Task<ReservationResponse> PayAsync(Guid id, PayReservationRequest request, CancellationToken ct)
    {
        var reservation = await reservationRepository.GetByIdForPaymentAsync(id, ct)
            ?? throw new NotFoundAppException("Reserva no encontrada.");

        if (reservation.TouristId != currentUser.UserId)
            throw new ForbiddenAppException("Esta reserva no te pertenece.");

        if (reservation.Status != ReservationStatus.PENDING_PAYMENT)
            throw new ConflictAppException($"La reserva está en estado {reservation.Status}; no admite pago.");

        var now = DateTimeOffset.UtcNow;

        // Precondición documentada de UC-T-19 ("no expirada"): distinto del 409 anterior — acá el estado
        // sigue siendo PENDING_PAYMENT, pero el hold de cupo (ExpiresAt) ya venció.
        if (reservation.ExpiresAt is { } expiresAt && expiresAt < now)
            throw new GoneAppException("La reserva expiró; el cupo retenido ya no es válido para pagar.");

        // UC-SYS-02: revalidación de precio contra el valor vigente de cada Experience/Package.
        var revalidationByItemId = reservation.Items.ToDictionary(i => i.Id, BuildRevalidation);

        var anyPriceChanged = revalidationByItemId.Values.Any(r => r.Changed);

        if (anyPriceChanged && !request.AcceptPriceChanges)
        {
            // Bloqueante: no se llama al gateway ni se persiste nada — se le devuelve el precio vigente
            // al caller para que el turista lo acepte explícitamente antes de cobrar/confirmar.
            logger.LogInformation(
                "Pago de la reserva {ReservationId} detenido: el precio vigente cambió y no fue aceptado.", reservation.Id);
            return ToResponse(reservation, revalidationByItemId, requiresPriceAcceptance: true);
        }

        if (anyPriceChanged)
        {
            // El turista aceptó explícitamente el nuevo precio: se recongela antes de cobrar/confirmar.
            foreach (var item in reservation.Items)
            {
                var revalidation = revalidationByItemId[item.Id];
                if (!revalidation.Changed) continue;

                item.UnitPrice = revalidation.CurrentUnitPrice;
                item.Currency = revalidation.CurrentCurrency;
                item.Subtotal = revalidation.CurrentUnitPrice * item.Travelers;
            }
        }

        // Una reserva directa siempre tiene un único Item/moneda; agregación multi-moneda queda para
        // cuando exista una reserva de itinerario IA con varios proveedores.
        var amount = reservation.Items.Sum(i => i.Subtotal);
        var currency = reservation.Items.First().Currency;

        // El gateway se llama ANTES de abrir la transacción de DB: nunca sostener locks durante I/O externo.
        var chargeResult = await paymentGateway.ChargeAsync(
            new PaymentChargeRequest(reservation.Id, amount, currency, request.Success), ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (chargeResult.Approved)
        {
            // UC-SYS-07: confirmación automática, sin aprobación manual del Provider (Decisión 6).
            reservation.Status = ReservationStatus.CONFIRMED;
            reservation.ConfirmedAt = now;
            foreach (var item in reservation.Items)
                item.Status = ReservationItemStatus.CONFIRMED;
        }
        else
        {
            // El rechazo NO cambia Reservation.Status ni ReservationItem.Status: la reserva sigue
            // PENDING_PAYMENT y admite reintentar el pago mientras no venza ExpiresAt (decisión del
            // usuario: no liberar el cupo de inmediato). El fallo solo queda registrado acá (respuesta)
            // y en el log — cuando exista una entidad Payment, los intentos fallidos se registrarán ahí
            // sin volver a tocar el estado principal de la reserva.
            logger.LogWarning(
                "Pago rechazado para la reserva {ReservationId}: {Reason}", reservation.Id, chargeResult.FailureReason);
        }

        // Si se aceptó un precio nuevo, ese recongelamiento se persiste aunque el cobro haya sido rechazado.
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ToResponse(reservation,
            paymentApproved: chargeResult.Approved,
            paymentFailureReason: chargeResult.FailureReason);
    }

    private sealed record PriceRevalidation(bool Changed, decimal CurrentUnitPrice, string CurrentCurrency);

    /// <summary>UC-SYS-02 — precio/moneda vigentes se leen del producto real (Experience o Package), nunca de un valor cacheado.</summary>
    private static PriceRevalidation BuildRevalidation(ReservationItem item)
    {
        var (currentPrice, currentCurrency) = item switch
        {
            { ProductType: ProductType.EXPERIENCE, Experience: { } experience } => (experience.Price, experience.Currency),
            { ProductType: ProductType.PACKAGE, Package: { } package } => (package.Price, package.Currency),
            _ => (item.UnitPrice, item.Currency)
        };

        var changed = currentPrice != item.UnitPrice || currentCurrency != item.Currency;
        return new PriceRevalidation(changed, currentPrice, currentCurrency);
    }

    private static ReservationResponse ToResponse(
        Reservation reservation,
        IReadOnlyDictionary<Guid, PriceRevalidation>? revalidationByItemId = null,
        bool requiresPriceAcceptance = false,
        bool? paymentApproved = null,
        string? paymentFailureReason = null) => new()
    {
        Id = reservation.Id,
        Status = reservation.Status.ToString(),
        ExpiresAt = reservation.ExpiresAt,
        CreatedAt = reservation.CreatedAt,
        ConfirmedAt = reservation.ConfirmedAt,
        CancelledAt = reservation.CancelledAt,
        Items = [.. reservation.Items.Select(i => ToItemResponse(i, revalidationByItemId?.GetValueOrDefault(i.Id)))],
        Totals = [.. reservation.Items
            .GroupBy(i => i.Currency)
            .Select(g => new ReservationTotalResponse { Currency = g.Key, Amount = g.Sum(i => i.Subtotal) })],
        RequiresPriceAcceptance = requiresPriceAcceptance,
        PaymentApproved = paymentApproved,
        PaymentFailureReason = paymentFailureReason
    };

    /// <summary>
    /// item.Reservation viene poblado por fixup de EF Core (mismo query, ver ReservationRepository/ReservationItemRepository)
    /// aunque no siempre incluye Tourist — en la vista del propio TOURIST ese dato es irrelevante y queda vacío.
    /// </summary>
    private static ReservationItemResponse ToItemResponse(ReservationItem item, PriceRevalidation? revalidation = null) => new()
    {
        Id = item.Id,
        ReservationId = item.ReservationId,
        ReservationStatus = item.Reservation?.Status.ToString() ?? string.Empty,
        ProductType = item.ProductType.ToString(),
        ExperienceId = item.ExperienceId,
        ExperienceTitle = item.Experience?.Title,
        PackageId = item.PackageId,
        PackageTitle = item.Package?.Title,
        CompanyId = item.CompanyId,
        CompanyName = item.Company?.Name ?? string.Empty,
        TouristId = item.Reservation?.TouristId ?? Guid.Empty,
        TouristName = item.Reservation?.Tourist is { } tourist ? $"{tourist.FirstName} {tourist.LastName}".Trim() : string.Empty,
        Travelers = item.Travelers,
        UnitPrice = item.UnitPrice,
        Currency = item.Currency,
        Subtotal = item.Subtotal,
        Status = item.Status.ToString(),
        Date = item.ExperienceAvailability?.Date ?? item.PackageAvailability?.DepartureDate,
        StartTime = item.ExperienceAvailability?.StartTime,
        CreatedAt = item.CreatedAt,
        PriceChanged = revalidation?.Changed ?? false,
        CurrentUnitPrice = revalidation?.Changed == true ? revalidation.CurrentUnitPrice : null,
        CurrentCurrency = revalidation?.Changed == true ? revalidation.CurrentCurrency : null
    };
}
