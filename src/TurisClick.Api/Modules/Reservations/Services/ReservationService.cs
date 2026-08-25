using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Experiences.Repositories;
using TurisClick.Api.Modules.Reservations.Dtos;
using TurisClick.Api.Modules.Reservations.Entities;
using TurisClick.Api.Modules.Reservations.Repositories;
using TurisClick.Api.Shared.Exceptions;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Reservations.Services;

public class ReservationService(
    IReservationRepository reservationRepository,
    IReservationItemRepository reservationItemRepository,
    IExperienceAvailabilityRepository availabilityRepository,
    ICurrentUserContext currentUser,
    ICompanyOwnershipGuard ownershipGuard,
    TurisClickDbContext db) : IReservationService
{
    /// <summary>Ventana del hold de cupo mientras la reserva está PENDING_PAYMENT. La liberación efectiva por expiración es UC-SYS-08 (Oleada 8) — acá solo se registra el límite.</summary>
    private static readonly TimeSpan HoldWindow = TimeSpan.FromMinutes(30);

    public async Task<ReservationResponse> CreateAsync(CreateReservationRequest request, CancellationToken ct)
    {
        var availability = await availabilityRepository.GetByIdWithExperienceAsync(request.ExperienceAvailabilityId, ct)
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
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var affectedRows = await db.ExperienceAvailabilities
            .Where(a => a.Id == availability.Id && a.ReservedSlots + request.Travelers <= a.TotalSlots)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ReservedSlots, a => a.ReservedSlots + request.Travelers), ct);

        if (affectedRows == 0)
            throw new ConflictAppException("No hay cupo suficiente para la cantidad de viajeros solicitada.");

        var now = DateTimeOffset.UtcNow;

        // UC-SYS-02: precio y moneda siempre se leen frescos desde Experience — nunca se confía en un valor del cliente.
        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            TouristId = currentUser.UserId,
            Status = ReservationStatus.PENDING_PAYMENT,
            ExpiresAt = now.Add(HoldWindow),
            CreatedAt = now
        };

        reservation.Items.Add(new ReservationItem
        {
            Id = Guid.NewGuid(),
            ReservationId = reservation.Id,
            CompanyId = experience.CompanyId,
            ProductType = ProductType.EXPERIENCE,
            ExperienceId = experience.Id,
            ExperienceAvailabilityId = availability.Id,
            Travelers = request.Travelers,
            UnitPrice = experience.Price,
            Currency = experience.Currency,
            Subtotal = experience.Price * request.Travelers,
            Status = ReservationItemStatus.PENDING_PAYMENT,
            CreatedAt = now
        });

        await reservationRepository.AddAsync(reservation, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var created = await reservationRepository.GetByIdForReadAsync(reservation.Id, ct)
            ?? throw new InvalidOperationException("La reserva recién creada no pudo leerse.");

        return ToResponse(created);
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
            Items = items.Select(ToResponse).ToList(),
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
            Items = items.Select(ToItemResponse).ToList(),
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

    private static ReservationResponse ToResponse(Reservation reservation) => new()
    {
        Id = reservation.Id,
        Status = reservation.Status.ToString(),
        ExpiresAt = reservation.ExpiresAt,
        CreatedAt = reservation.CreatedAt,
        ConfirmedAt = reservation.ConfirmedAt,
        CancelledAt = reservation.CancelledAt,
        Items = [.. reservation.Items.Select(ToItemResponse)],
        Totals = [.. reservation.Items
            .GroupBy(i => i.Currency)
            .Select(g => new ReservationTotalResponse { Currency = g.Key, Amount = g.Sum(i => i.Subtotal) })]
    };

    /// <summary>
    /// item.Reservation viene poblado por fixup de EF Core (mismo query, ver ReservationRepository/ReservationItemRepository)
    /// aunque no siempre incluye Tourist — en la vista del propio TOURIST ese dato es irrelevante y queda vacío.
    /// </summary>
    private static ReservationItemResponse ToItemResponse(ReservationItem item) => new()
    {
        Id = item.Id,
        ReservationId = item.ReservationId,
        ReservationStatus = item.Reservation?.Status.ToString() ?? string.Empty,
        ProductType = item.ProductType.ToString(),
        ExperienceId = item.ExperienceId,
        ExperienceTitle = item.Experience?.Title,
        CompanyId = item.CompanyId,
        CompanyName = item.Company?.Name ?? string.Empty,
        TouristId = item.Reservation?.TouristId ?? Guid.Empty,
        TouristName = item.Reservation?.Tourist is { } tourist ? $"{tourist.FirstName} {tourist.LastName}".Trim() : string.Empty,
        Travelers = item.Travelers,
        UnitPrice = item.UnitPrice,
        Currency = item.Currency,
        Subtotal = item.Subtotal,
        Status = item.Status.ToString(),
        Date = item.ExperienceAvailability?.Date,
        StartTime = item.ExperienceAvailability?.StartTime,
        CreatedAt = item.CreatedAt
    };
}
