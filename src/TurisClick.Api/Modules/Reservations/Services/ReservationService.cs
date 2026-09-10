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
using TurisClick.Api.Modules.Companies.Entities;

namespace TurisClick.Api.Modules.Reservations.Services;

public class ReservationService(
    IReservationRepository reservationRepository,
    IReservationItemRepository reservationItemRepository,
    IExperienceAvailabilityRepository experienceAvailabilityRepository,
    IPackageAvailabilityRepository packageAvailabilityRepository,
    IPaymentGateway paymentGateway,
    IReservationBookingService bookingService,
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
        // UC-A-08: si la empresa está suspendida su catálogo no existe para el turista — tampoco por
        // la puerta de atrás de reservar directo una availability cuyo id ya conocía.
        if (experience.Status != PublicationStatus.PUBLISHED
            || experience.Company?.Status == CompanyStatus.SUSPENDED)
            throw new NotFoundAppException("Experiencia no encontrada.");

        // UC-T-08 excepción: "slot ya no existe/expiró" → 410 (distinto de sin-cupo, que es 409).
        if (availability.Status != AvailabilitySlotStatus.OPEN)
            throw new GoneAppException("El slot de disponibilidad ya no está disponible.");

        if (availability.Date < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new GoneAppException("El slot de disponibilidad ya expiró.");

        // UC-SYS-06: UPDATE condicional atómico dentro de una transacción — ver backend-architecture.md §13.
        var tx = await db.Database.BeginTransactionAsync(ct);

        var affectedRows = await db.ExperienceAvailabilities
            .Where(a => a.Id == availability.Id
                && a.Status == AvailabilitySlotStatus.OPEN
                && a.ReservedSlots + travelers <= a.TotalSlots)
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
        if (package.Status != PublicationStatus.PUBLISHED
            || package.Company?.Status == CompanyStatus.SUSPENDED)
            throw new NotFoundAppException("Paquete no encontrado.");

        if (availability.Status != AvailabilitySlotStatus.OPEN)
            throw new GoneAppException("La salida ya no está disponible.");

        if (availability.DepartureDate < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new GoneAppException("La salida ya expiró.");

        var tx = await db.Database.BeginTransactionAsync(ct);

        var affectedRows = await db.PackageAvailabilities
            .Where(a => a.Id == availability.Id
                && a.Status == AvailabilitySlotStatus.OPEN
                && a.ReservedSlots + travelers <= a.TotalSlots)
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

        // Una reserva ya expirada es un 410 y no un 409: el recurso existió pero su hold de cupo ya se
        // liberó, que es exactamente la semántica de Gone (mismo criterio que el chequeo de ExpiresAt
        // de más abajo). El resto de los estados no pagables siguen siendo un conflicto común.
        if (reservation.Status == ReservationStatus.EXPIRED)
            throw new GoneAppException(
                "La reserva expiró y su cupo ya fue liberado.", ErrorCodes.ReservationNoLongerPayable);

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

        // Una reserva de itinerario IA (UC-T-18) puede combinar productos de varias empresas en
        // MONEDAS distintas. Sumar los subtotales en un único importe implicaría una conversión que
        // TurisClick no hace, así que se cobra un cargo por moneda. Una reserva directa tiene una sola
        // moneda y sigue produciendo exactamente un cargo, igual que antes.
        var chargesByCurrency = reservation.Items
            .GroupBy(i => i.Currency)
            .Select(g => new { Currency = g.Key, Amount = g.Sum(i => i.Subtotal) })
            .OrderBy(c => c.Currency)
            .ToList();

        // El gateway se llama ANTES de abrir la transacción de DB: nunca sostener locks durante I/O externo.
        PaymentChargeResult chargeResult = new(Approved: true, FailureReason: null);
        foreach (var charge in chargesByCurrency)
        {
            chargeResult = await paymentGateway.ChargeAsync(
                new PaymentChargeRequest(reservation.Id, charge.Amount, charge.Currency, request.Success), ct);

            // Un rechazo en cualquier moneda deja la reserva entera PENDING_PAYMENT y reintentable: no
            // se confirma una parte del viaje. (Con una pasarela real habría que compensar los cargos
            // ya aprobados; con la simulada todos los grupos responden igual — ver limitaciones.)
            if (!chargeResult.Approved)
            {
                logger.LogWarning(
                    "Pago rechazado para la reserva {ReservationId} en {Currency}: {Reason}",
                    reservation.Id, charge.Currency, chargeResult.FailureReason);
                break;
            }
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (chargeResult.Approved)
        {
            // UC-SYS-07: confirmación automática, sin aprobación manual del Provider (Decisión 6).
            //
            // La transición se hace CONDICIONAL y no asignando la entidad trackeada: entre la lectura de
            // arriba y este punto pasó una llamada al gateway, y en esa ventana el proceso de expiración
            // (UC-SYS-08) pudo haber liberado el cupo y dejado la reserva en EXPIRED. Sin esta condición
            // se confirmaría una reserva sin cupo retenido. Pago y expiración compiten por la misma fila
            // y solo una transición puede ganar; si perdemos, no se confirma nada.
            var confirmed = await db.Reservations
                .Where(r => r.Id == reservation.Id && r.Status == ReservationStatus.PENDING_PAYMENT)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, ReservationStatus.CONFIRMED)
                    .SetProperty(r => r.ConfirmedAt, now), ct);

            if (confirmed != 1)
            {
                await tx.RollbackAsync(ct);

                logger.LogWarning(
                    "Pago de la reserva {ReservationId} llegó tarde: otra transición (expiración o cancelación) ganó la carrera.",
                    reservation.Id);

                throw new GoneAppException(
                    "La reserva expiró o dejó de estar pendiente de pago mientras se procesaba el cobro; el cupo retenido ya no es válido.",
                    ErrorCodes.ReservationNoLongerPayable);
            }

            // Se reflejan también en la entidad trackeada: el ExecuteUpdate de arriba no pasa por el
            // change tracker, así que sin esto la respuesta seguiría diciendo PENDING_PAYMENT. El
            // SaveChanges de más abajo reescribe los mismos valores sobre la fila que esta misma
            // transacción ya bloqueó, así que es inofensivo.
            reservation.Status = ReservationStatus.CONFIRMED;
            reservation.ConfirmedAt = now;

            foreach (var item in reservation.Items)
                item.Status = ReservationItemStatus.CONFIRMED;
        }
        // Si el cobro fue rechazado no se cambia Reservation.Status ni ReservationItem.Status: la
        // reserva sigue PENDING_PAYMENT y admite reintentar el pago mientras no venza ExpiresAt
        // (decisión del usuario: no liberar el cupo de inmediato). El fallo queda registrado en la
        // respuesta y en el log — cuando exista una entidad Payment, los intentos fallidos se
        // registrarán ahí sin volver a tocar el estado principal de la reserva.

        // Si se aceptó un precio nuevo, ese recongelamiento se persiste aunque el cobro haya sido rechazado.
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ToResponse(reservation,
            paymentApproved: chargeResult.Approved,
            paymentFailureReason: chargeResult.FailureReason);
    }

    /// <summary>
    /// UC-T-11 — el turista cancela su reserva completa y se libera todo el cupo.
    ///
    /// En esta oleada solo se admite PENDING_PAYMENT: cancelar una reserva ya CONFIRMED implicaría una
    /// devolución de dinero, y no existe todavía ni entidad Payment ni pasarela real, así que inventar
    /// una política comercial acá sería peor que no ofrecer la operación (decisión de dominio Oleada 8).
    /// </summary>
    public async Task<ReservationResponse> CancelAsync(Guid id, CancellationToken ct)
    {
        var reservation = await reservationRepository.GetByIdForCancellationAsync(id, ct)
            ?? throw new NotFoundAppException("Reserva no encontrada.");

        if (reservation.TouristId != currentUser.UserId)
            throw new ForbiddenAppException("Esta reserva no te pertenece.");

        if (reservation.Status == ReservationStatus.CONFIRMED)
            throw new ConflictAppException(
                "Una reserva ya confirmada no se puede cancelar todavía: falta definir la política de reembolso.",
                ErrorCodes.RefundPolicyRequired);

        if (reservation.Status != ReservationStatus.PENDING_PAYMENT)
            throw new ConflictAppException(
                $"Una reserva en estado {reservation.Status} no se puede cancelar.",
                ErrorCodes.ReservationNotCancellable);

        var now = DateTimeOffset.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Misma autoridad que en la expiración: gana quien consigue la transición. Si el pago la
        // confirmó o el proceso de expiración se adelantó, acá no se libera nada.
        var won = await db.Reservations
            .Where(r => r.Id == id && r.Status == ReservationStatus.PENDING_PAYMENT)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ReservationStatus.CANCELLED)
                .SetProperty(r => r.CancelledAt, now), ct);

        if (won != 1)
        {
            await tx.RollbackAsync(ct);
            throw new ConflictAppException(
                "La reserva cambió de estado mientras se cancelaba; volvé a consultarla.",
                ErrorCodes.ReservationNotCancellable);
        }

        // Solo las líneas todavía activas: una que el proveedor ya canceló conserva su estado y su motivo.
        var activeItems = reservation.Items
            .Where(i => i.Status == ReservationItemStatus.PENDING_PAYMENT)
            .ToList();

        await bookingService.ReleaseHoldsAsync(activeItems, ct);

        await db.ReservationItems
            .Where(i => i.ReservationId == id && i.Status == ReservationItemStatus.PENDING_PAYMENT)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, ReservationItemStatus.CANCELLED)
                .SetProperty(i => i.CancelledAt, now), ct);

        await tx.CommitAsync(ct);

        logger.LogInformation(
            "Reserva {ReservationId} cancelada por el turista: {Items} línea(s) liberada(s).", id, activeItems.Count);

        return await GetByIdForTouristAsync(id, ct);
    }

    /// <summary>
    /// UC-P-14 — cancelación excepcional del proveedor sobre SU línea. Los demás ítems y la Reservation
    /// padre siguen activos (domain-model.md: no existe PARTIALLY_CANCELLED, se deriva de los ítems).
    /// </summary>
    public async Task<ReservationItemResponse> CancelItemAsync(Guid itemId, CancelReservationItemRequest request, CancellationToken ct)
    {
        var item = await reservationItemRepository.GetByIdForUpdateAsync(itemId, ct)
            ?? throw new NotFoundAppException("Reserva no encontrada.");

        ownershipGuard.EnsureOwns(item.CompanyId);

        if (item.Status is not (ReservationItemStatus.PENDING_PAYMENT or ReservationItemStatus.CONFIRMED))
            throw new ConflictAppException(
                $"Una línea en estado {item.Status} no se puede cancelar.",
                ErrorCodes.ReservationNotCancellable);

        var now = DateTimeOffset.UtcNow;
        var reason = request.Reason.Trim();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Transición condicional sobre el MISMO estado que se leyó: si el turista canceló la reserva
        // entera o expiró en el medio, esta ejecución no libera cupo dos veces.
        var previousStatus = item.Status;
        var won = await db.ReservationItems
            .Where(i => i.Id == itemId && i.Status == previousStatus)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, ReservationItemStatus.CANCELLED)
                .SetProperty(i => i.CancelledAt, now)
                .SetProperty(i => i.CancellationReason, reason), ct);

        if (won != 1)
        {
            await tx.RollbackAsync(ct);
            throw new ConflictAppException(
                "La línea cambió de estado mientras se cancelaba; volvé a consultarla.",
                ErrorCodes.ReservationNotCancellable);
        }

        await bookingService.ReleaseHoldsAsync([item], ct);

        await tx.CommitAsync(ct);

        logger.LogInformation(
            "El proveedor {CompanyId} canceló la línea {ItemId} de la reserva {ReservationId}.",
            item.CompanyId, itemId, item.ReservationId);

        return await GetReceivedItemByIdAsync(itemId, ct);
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
        CancelledAt = item.CancelledAt,
        CancellationReason = item.CancellationReason,
        Date = item.ExperienceAvailability?.Date ?? item.PackageAvailability?.DepartureDate,
        StartTime = item.ExperienceAvailability?.StartTime,
        CreatedAt = item.CreatedAt,
        PriceChanged = revalidation?.Changed ?? false,
        CurrentUnitPrice = revalidation?.Changed == true ? revalidation.CurrentUnitPrice : null,
        CurrentCurrency = revalidation?.Changed == true ? revalidation.CurrentCurrency : null
    };
}
