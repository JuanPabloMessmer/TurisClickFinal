using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Companies.Entities;
using TurisClick.Api.Modules.Flights.ProviderFlights.Dtos;
using TurisClick.Api.Modules.Flights.ProviderFlights.Entities;
using TurisClick.Api.Modules.Flights.Services;
using TurisClick.Api.Modules.Reservations.Entities;
using TurisClick.Api.Modules.Reservations.Payments;
using TurisClick.Api.Modules.Reservations.Policies;
using TurisClick.Api.Modules.Reservations.Services;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Flights.ProviderFlights.Services;

public interface IProviderFlightService
{
    Task<List<ProviderFlightResponse>> ListMineAsync(CancellationToken ct);
    Task<ProviderFlightResponse> CreateAsync(ProviderFlightRequest request, CancellationToken ct);
    Task<ProviderFlightResponse> GetMineAsync(Guid id, CancellationToken ct);
    Task<ProviderFlightResponse> UpdateAsync(Guid id, ProviderFlightRequest request, CancellationToken ct);
    Task<ProviderFlightResponse> PublishAsync(Guid id, CancellationToken ct);
    Task<ProviderFlightResponse> ArchiveAsync(Guid id, CancellationToken ct);

    Task<List<ProviderFlightDepartureResponse>> ListDeparturesAsync(Guid flightId, CancellationToken ct);
    Task<ProviderFlightDepartureResponse> CreateDepartureAsync(Guid flightId, ProviderFlightDepartureRequest request, CancellationToken ct);
    Task<ProviderFlightDepartureResponse> UpdateDepartureAsync(Guid departureId, ProviderFlightDepartureRequest request, CancellationToken ct);
    Task DeleteDepartureAsync(Guid departureId, CancellationToken ct);
    Task<ProviderFlightDepartureResponse> CloseDepartureAsync(Guid departureId, CancellationToken ct);

    Task<FlightItemIssuanceResponse> RecordIssuanceAsync(Guid itemId, RecordIssuanceRequest request, CancellationToken ct);
    Task<FlightItemIssuanceResponse> RecordIssuanceFailureAsync(Guid itemId, IssuanceFailureRequest request, CancellationToken ct);
}

/// <summary>
/// El operador y sus vuelos: inventario, salidas, asientos, tarifas, publicación y emisión.
///
/// Tres reglas que sostiene este servicio:
/// <list type="number">
/// <item>Sólo vende vuelos una empresa aprobada **y autorizada por un ADMIN** (403 FLIGHT_SELLER_NOT_AUTHORIZED).
/// La emisión de lo ya vendido es la excepción: si se revoca la autorización, lo vendido se sigue emitiendo.</item>
/// <item>Lo que se vendió no cambia por debajo: con reservas, la ruta, la aerolínea, el número de vuelo, la
/// cabina y el equipaje de un vuelo, y los horarios y tarifas de una salida, ya no se editan.</item>
/// <item>"Emitido" lo dice el operador y nadie más: ISSUED sólo se escribe acá, con los números de pasaje.</item>
/// </list>
/// </summary>
public class ProviderFlightService(
    TurisClickDbContext db,
    ICurrentUserContext currentUser,
    ICompanyOwnershipGuard ownershipGuard,
    IReservationBookingService bookingService,
    IPaymentGateway paymentGateway,
    IPaymentLedger ledger,
    ILogger<ProviderFlightService> logger) : IProviderFlightService
{
    // ================================================================ vuelos

    public async Task<List<ProviderFlightResponse>> ListMineAsync(CancellationToken ct)
    {
        var company = await RequireSellerAsync(ct);

        var flights = await db.ProviderFlights
            .AsNoTracking()
            .Include(f => f.Company)
            .Include(f => f.Departures)
            .Where(f => f.CompanyId == company.Id)
            .OrderByDescending(f => f.UpdatedAt)
            .ToListAsync(ct);

        return [.. flights.Select(ToResponse)];
    }

    public async Task<ProviderFlightResponse> CreateAsync(ProviderFlightRequest request, CancellationToken ct)
    {
        var company = await RequireSellerAsync(ct);
        var now = DateTimeOffset.UtcNow;

        var flight = new ProviderFlight
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            Status = ProviderFlightStatus.DRAFT,
            CreatedAt = now,
        };
        Apply(flight, request, now);

        db.ProviderFlights.Add(flight);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Vuelo de operador {FlightId} creado por {CompanyId} ({Origin}→{Destination}).",
            flight.Id, company.Id, flight.OriginIata, flight.DestinationIata);

        flight.Company = company;
        return ToResponse(flight);
    }

    public async Task<ProviderFlightResponse> GetMineAsync(Guid id, CancellationToken ct)
    {
        await RequireSellerAsync(ct);
        return ToResponse(await LoadOwnedFlightAsync(id, ct));
    }

    public async Task<ProviderFlightResponse> UpdateAsync(Guid id, ProviderFlightRequest request, CancellationToken ct)
    {
        await RequireSellerAsync(ct);
        var flight = await LoadOwnedFlightAsync(id, ct);

        if (await HasReservationsAsync(flight.Id, ct) && ChangesWhatWasSold(flight, request))
            throw new ConflictAppException(
                "Este vuelo ya tiene reservas: la ruta, la aerolínea, el número de vuelo, la cabina y el equipaje no se " +
                "pueden cambiar. Las condiciones de la tarifa y la política de cancelación, sí.");

        Apply(flight, request, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);

        return ToResponse(flight);
    }

    public async Task<ProviderFlightResponse> PublishAsync(Guid id, CancellationToken ct)
    {
        await RequireSellerAsync(ct);
        var flight = await LoadOwnedFlightAsync(id, ct);

        if (flight.Status == ProviderFlightStatus.PUBLISHED) return ToResponse(flight);

        if (!flight.Departures.Any(IsSellable))
            throw new ConflictAppException(
                "Para publicar el vuelo hace falta al menos una salida futura abierta con asientos libres.");

        flight.Status = ProviderFlightStatus.PUBLISHED;
        flight.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Vuelo de operador {FlightId} publicado.", flight.Id);
        return ToResponse(flight);
    }

    public async Task<ProviderFlightResponse> ArchiveAsync(Guid id, CancellationToken ct)
    {
        await RequireSellerAsync(ct);
        var flight = await LoadOwnedFlightAsync(id, ct);

        flight.Status = ProviderFlightStatus.ARCHIVED;
        flight.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return ToResponse(flight);
    }

    // ================================================================ salidas

    public async Task<List<ProviderFlightDepartureResponse>> ListDeparturesAsync(Guid flightId, CancellationToken ct)
    {
        await RequireSellerAsync(ct);
        var flight = await LoadOwnedFlightAsync(flightId, ct);

        var sold = await DeparturesWithReservationsAsync(flight.Departures.Select(d => d.Id), ct);

        return [.. flight.Departures.OrderBy(d => d.DepartureAt).Select(d => ToResponse(d, sold.Contains(d.Id)))];
    }

    public async Task<ProviderFlightDepartureResponse> CreateDepartureAsync(
        Guid flightId, ProviderFlightDepartureRequest request, CancellationToken ct)
    {
        await RequireSellerAsync(ct);
        var flight = await LoadOwnedFlightAsync(flightId, ct);

        if (flight.Status == ProviderFlightStatus.ARCHIVED)
            throw new ConflictAppException("El vuelo está archivado: no se le agregan salidas.");

        var now = DateTimeOffset.UtcNow;
        var departure = new ProviderFlightDeparture
        {
            Id = Guid.NewGuid(),
            ProviderFlightId = flight.Id,
            ReservedSeats = 0,
            Status = ProviderFlightDepartureStatus.OPEN,
            CreatedAt = now,
        };
        Apply(departure, request, now);

        db.ProviderFlightDepartures.Add(departure);
        flight.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return ToResponse(departure, hasReservations: false);
    }

    public async Task<ProviderFlightDepartureResponse> UpdateDepartureAsync(
        Guid departureId, ProviderFlightDepartureRequest request, CancellationToken ct)
    {
        await RequireSellerAsync(ct);
        var departure = await LoadOwnedDepartureAsync(departureId, ct);
        var sold = await HasReservationsOnDepartureAsync(departure.Id, ct);

        if (sold && (request.DepartureAt != departure.DepartureAt
                     || request.ArrivalAt != departure.ArrivalAt
                     || request.AdultPrice != departure.AdultPrice
                     || request.ChildPrice != departure.ChildPrice
                     || request.InfantPrice != departure.InfantPrice
                     || !string.Equals(request.Currency, departure.Currency, StringComparison.Ordinal)))
            throw new ConflictAppException(
                "Esta salida ya tiene reservas: los horarios y las tarifas no se pueden cambiar. Los asientos totales, sí.");

        var now = DateTimeOffset.UtcNow;

        // El total de asientos se cambia con un UPDATE condicional y no leyendo y escribiendo: entre la lectura y
        // la escritura alguien pudo haber tomado asientos, y el total nunca puede quedar debajo de lo vendido.
        var changed = await db.ProviderFlightDepartures
            .Where(d => d.Id == departure.Id && d.ReservedSeats <= request.TotalSeats)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.TotalSeats, request.TotalSeats), ct);

        if (changed != 1)
            throw new ConflictAppException(
                "No se puede dejar la salida con menos asientos que los que ya están reservados o vendidos.");

        departure.DepartureAt = request.DepartureAt;
        departure.ArrivalAt = request.ArrivalAt;
        departure.AdultPrice = request.AdultPrice;
        departure.ChildPrice = request.ChildPrice;
        departure.InfantPrice = request.InfantPrice;
        departure.Currency = request.Currency;
        departure.UpdatedAt = now;

        // Total ya escrito por el UPDATE condicional: se alinea sin volver a escribirlo.
        var total = db.Entry(departure).Property(d => d.TotalSeats);
        total.OriginalValue = total.CurrentValue = request.TotalSeats;

        await db.SaveChangesAsync(ct);

        var fresh = await db.ProviderFlightDepartures.AsNoTracking().FirstAsync(d => d.Id == departure.Id, ct);
        return ToResponse(fresh, sold);
    }

    public async Task DeleteDepartureAsync(Guid departureId, CancellationToken ct)
    {
        await RequireSellerAsync(ct);
        var departure = await LoadOwnedDepartureAsync(departureId, ct);

        if (await HasReservationsOnDepartureAsync(departure.Id, ct))
            throw new ConflictAppException("Esta salida tiene reservas: no se borra, se cierra.");

        db.ProviderFlightDepartures.Remove(departure);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ProviderFlightDepartureResponse> CloseDepartureAsync(Guid departureId, CancellationToken ct)
    {
        await RequireSellerAsync(ct);
        var departure = await LoadOwnedDepartureAsync(departureId, ct);

        departure.Status = ProviderFlightDepartureStatus.CLOSED;
        departure.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return ToResponse(departure, await HasReservationsOnDepartureAsync(departure.Id, ct));
    }

    // ================================================================ emisión

    public async Task<FlightItemIssuanceResponse> RecordIssuanceAsync(
        Guid itemId, RecordIssuanceRequest request, CancellationToken ct)
    {
        var item = await LoadOwnedFlightItemAsync(itemId, ct);

        var passengers = await db.ReservationPassengers.CountAsync(p => p.ReservationItemId == item.Id, ct);
        if (request.TicketNumbers.Count != passengers)
            throw new ValidationAppException(
                $"La reserva tiene {passengers} pasajero(s): hace falta un número de pasaje por cada uno, infantes incluidos.");

        var tickets = string.Join(',', request.TicketNumbers.Select(t => t.Trim().ToUpperInvariant()));
        var reference = request.BookingReference?.Trim().ToUpperInvariant();
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var now = DateTimeOffset.UtcNow;

        // Sólo un ítem pagado y pendiente de emitir: la transición condicional es la que decide. Registrar dos
        // veces, o emitir algo que se canceló en el medio, termina en 409 y no en dos estados mezclados.
        var won = await db.ReservationItems
            .Where(i => i.Id == item.Id
                && i.Status == ReservationItemStatus.CONFIRMED
                && i.IssuanceStatus == FlightIssuanceStatus.PENDING)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.IssuanceStatus, FlightIssuanceStatus.ISSUED)
                .SetProperty(i => i.TicketNumbers, tickets)
                .SetProperty(i => i.BookingReference, reference)
                .SetProperty(i => i.IssuanceNote, note)
                .SetProperty(i => i.IssuedAt, now)
                .SetProperty(i => i.IssuedByUserId, currentUser.UserId), ct);

        if (won != 1)
            throw new ConflictAppException(
                "Este vuelo no está pendiente de emisión: sólo se emite un pasaje pagado que todavía no se emitió.");

        logger.LogInformation("Emisión registrada para el ítem {ItemId} ({Tickets} pasaje(s)).", item.Id, passengers);

        return new FlightItemIssuanceResponse
        {
            ReservationItemId = item.Id,
            IssuanceStatus = FlightIssuanceStatus.ISSUED.ToString(),
            TicketNumbers = ReservationService.SplitTickets(tickets),
            BookingReference = reference,
            IssuedAt = now,
            Note = note,
        };
    }

    /// <summary>
    /// El operador no pudo emitir. El tramo se cancela, sus asientos vuelven a la venta y se devuelve todo lo
    /// pagado por él (el pago es simulado: el asiento en el libro existe igual). Si era lo último activo de la
    /// reserva, la reserva queda cancelada.
    /// </summary>
    public async Task<FlightItemIssuanceResponse> RecordIssuanceFailureAsync(
        Guid itemId, IssuanceFailureRequest request, CancellationToken ct)
    {
        var item = await LoadOwnedFlightItemAsync(itemId, ct);
        var reason = request.Reason.Trim();
        var now = DateTimeOffset.UtcNow;

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var won = await db.ReservationItems
                .Where(i => i.Id == item.Id
                    && i.Status == ReservationItemStatus.CONFIRMED
                    && i.IssuanceStatus == FlightIssuanceStatus.PENDING)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Status, ReservationItemStatus.CANCELLED)
                    .SetProperty(i => i.IssuanceStatus, FlightIssuanceStatus.FAILED)
                    .SetProperty(i => i.CancelledAt, now)
                    .SetProperty(i => i.CancellationReason, $"No se pudo emitir el pasaje: {reason}")
                    .SetProperty(i => i.IssuanceNote, reason), ct);

            if (won != 1)
                throw new ConflictAppException(
                    "Este vuelo no está pendiente de emisión: sólo se registra una falla de emisión sobre un pasaje pagado sin emitir.");

            await bookingService.ReleaseHoldsAsync([item], ct);

            var anyActiveLeft = await db.ReservationItems.AnyAsync(i =>
                i.ReservationId == item.ReservationId
                && (i.Status == ReservationItemStatus.PENDING_PAYMENT || i.Status == ReservationItemStatus.CONFIRMED), ct);

            if (!anyActiveLeft)
                await db.Reservations
                    .Where(r => r.Id == item.ReservationId && r.Status == ReservationStatus.CONFIRMED)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Status, ReservationStatus.CANCELLED)
                        .SetProperty(r => r.CancelledAt, now), ct);

            await tx.CommitAsync(ct);
        }

        await RefundFailedIssuanceAsync(item, reason, ct);

        logger.LogWarning("El operador no pudo emitir el ítem {ItemId}: se canceló con reembolso total.", item.Id);

        return new FlightItemIssuanceResponse
        {
            ReservationItemId = item.Id,
            IssuanceStatus = FlightIssuanceStatus.FAILED.ToString(),
            Note = reason,
        };
    }

    /// <summary>
    /// Reembolso total del tramo. La clave es la del ítem: reintentar no devuelve dos veces, y un reembolso
    /// fallido de la pasarela queda asentado como tal en el libro para poder reintentarlo.
    /// </summary>
    private async Task RefundFailedIssuanceAsync(ReservationItem item, string reason, CancellationToken ct)
    {
        var key = $"ref_issfail_{item.Id:N}";
        if (item.Subtotal <= 0m || await ledger.RefundAlreadyRecordedAsync(key, ct)) return;

        PaymentRefundResult result;
        try
        {
            result = await paymentGateway.RefundAsync(
                new PaymentRefundRequest(item.ReservationId, item.Subtotal, item.Currency, $"Pasaje no emitido: {reason}", key), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falló el reembolso por emisión fallida del ítem {ItemId}.", item.Id);
            result = new PaymentRefundResult(false, "La pasarela no respondió.", null);
        }

        ledger.RecordRefund(
            item.ReservationId, item.Subtotal, item.Currency, PaymentComponent.PROVIDER_FLIGHT, item.Id,
            "Simulated", key, result.Succeeded, result.FailureReason, result.Reference);

        await db.SaveChangesAsync(ct);
    }

    // ================================================================ reglas y carga

    /// <summary>La empresa del operador, si está aprobada y autorizada a vender vuelos. Si no, 403 con su código.</summary>
    private async Task<Company> RequireSellerAsync(CancellationToken ct)
    {
        var companyId = currentUser.CompanyId
            ?? throw new ForbiddenAppException("El usuario autenticado no tiene una empresa asociada.");

        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, ct)
            ?? throw new ForbiddenAppException("El usuario autenticado no tiene una empresa asociada.");

        if (company.Status != CompanyStatus.APPROVED || !company.CanSellFlights)
            throw new ForbiddenAppException(
                "Tu empresa todavía no está autorizada a vender vuelos. La autoriza un administrador de TurisClick.",
                ErrorCodes.FlightSellerNotAuthorized);

        return company;
    }

    private async Task<ProviderFlight> LoadOwnedFlightAsync(Guid id, CancellationToken ct)
    {
        var flight = await db.ProviderFlights
            .Include(f => f.Company)
            .Include(f => f.Departures)
            .FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new NotFoundAppException("Vuelo no encontrado.");

        ownershipGuard.EnsureOwns(flight.CompanyId);
        return flight;
    }

    private async Task<ProviderFlightDeparture> LoadOwnedDepartureAsync(Guid id, CancellationToken ct)
    {
        var departure = await db.ProviderFlightDepartures
            .Include(d => d.ProviderFlight)
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new NotFoundAppException("Salida no encontrada.");

        ownershipGuard.EnsureOwns(departure.ProviderFlight!.CompanyId);
        return departure;
    }

    /// <summary>Un ítem de vuelo de la empresa del operador. No exige la autorización: lo vendido se emite igual.</summary>
    private async Task<ReservationItem> LoadOwnedFlightItemAsync(Guid itemId, CancellationToken ct)
    {
        var item = await db.ReservationItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new NotFoundAppException("Reserva no encontrada.");

        ownershipGuard.EnsureOwns(item.CompanyId);

        if (item.ProductType != ProductType.FLIGHT)
            throw new ValidationAppException("Esta línea no es un vuelo de operador.");

        return item;
    }

    private Task<bool> HasReservationsAsync(Guid flightId, CancellationToken ct) =>
        db.ReservationItems.AnyAsync(i => i.ProviderFlightId == flightId, ct);

    private Task<bool> HasReservationsOnDepartureAsync(Guid departureId, CancellationToken ct) =>
        db.ReservationItems.AnyAsync(i => i.ProviderFlightDepartureId == departureId, ct);

    private async Task<HashSet<Guid>> DeparturesWithReservationsAsync(IEnumerable<Guid> departureIds, CancellationToken ct)
    {
        var ids = departureIds.ToList();
        var used = await db.ReservationItems
            .Where(i => i.ProviderFlightDepartureId != null && ids.Contains(i.ProviderFlightDepartureId.Value))
            .Select(i => i.ProviderFlightDepartureId!.Value)
            .Distinct()
            .ToListAsync(ct);
        return [.. used];
    }

    private static bool ChangesWhatWasSold(ProviderFlight flight, ProviderFlightRequest request) =>
        AirportCatalog.Normalize(request.OriginIata) != flight.OriginIata
        || AirportCatalog.Normalize(request.DestinationIata) != flight.DestinationIata
        || request.CarrierName.Trim() != flight.CarrierName
        || NullIfBlank(request.CarrierIata) != flight.CarrierIata
        || request.FlightNumber.Trim() != flight.FlightNumber
        || Enum.Parse<FlightCabinClass>(request.CabinClass, ignoreCase: true) != flight.CabinClass
        || request.CheckedBags != flight.CheckedBags
        || request.CarryOnBags != flight.CarryOnBags;

    internal static bool IsSellable(ProviderFlightDeparture departure) =>
        departure.Status == ProviderFlightDepartureStatus.OPEN
        && departure.DepartureAt > DateTime.UtcNow
        && departure.ReservedSeats < departure.TotalSeats;

    private static void Apply(ProviderFlight flight, ProviderFlightRequest request, DateTimeOffset now)
    {
        flight.OriginIata = AirportCatalog.Normalize(request.OriginIata);
        flight.DestinationIata = AirportCatalog.Normalize(request.DestinationIata);
        flight.CarrierName = request.CarrierName.Trim();
        flight.CarrierIata = NullIfBlank(request.CarrierIata);
        flight.FlightNumber = request.FlightNumber.Trim();
        flight.CabinClass = Enum.Parse<FlightCabinClass>(request.CabinClass, ignoreCase: true);
        flight.CheckedBags = request.CheckedBags;
        flight.CarryOnBags = request.CarryOnBags;
        flight.CancellationPolicy = CancellationPolicyRules.Serialize(request.CancellationPolicy);
        flight.FareRules = NullIfBlank(request.FareRules);
        flight.UpdatedAt = now;
    }

    private static void Apply(ProviderFlightDeparture departure, ProviderFlightDepartureRequest request, DateTimeOffset now)
    {
        departure.DepartureAt = DateTime.SpecifyKind(request.DepartureAt, DateTimeKind.Unspecified);
        departure.ArrivalAt = DateTime.SpecifyKind(request.ArrivalAt, DateTimeKind.Unspecified);
        departure.TotalSeats = request.TotalSeats;
        departure.AdultPrice = request.AdultPrice;
        departure.ChildPrice = request.ChildPrice;
        departure.InfantPrice = request.InfantPrice;
        departure.Currency = request.Currency;
        departure.UpdatedAt = now;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ================================================================ mapeo

    internal static ProviderFlightResponse ToResponse(ProviderFlight flight) => new()
    {
        Id = flight.Id,
        CompanyId = flight.CompanyId,
        CompanyName = flight.Company?.Name ?? string.Empty,
        OriginIata = flight.OriginIata,
        OriginLabel = AirportCatalog.Describe(flight.OriginIata),
        DestinationIata = flight.DestinationIata,
        DestinationLabel = AirportCatalog.Describe(flight.DestinationIata),
        CarrierName = flight.CarrierName,
        CarrierIata = flight.CarrierIata,
        FlightNumber = flight.FlightNumber,
        CabinClass = flight.CabinClass.ToString(),
        CheckedBags = flight.CheckedBags,
        CarryOnBags = flight.CarryOnBags,
        CancellationPolicy = CancellationPolicyRules.Deserialize(flight.CancellationPolicy),
        FareRules = flight.FareRules,
        Status = flight.Status.ToString(),
        Suspended = flight.SuspendedAt is not null,
        UpcomingDepartures = flight.Departures.Count(IsSellable),
        CreatedAt = flight.CreatedAt,
        UpdatedAt = flight.UpdatedAt,
        Demo = true,
    };

    internal static ProviderFlightDepartureResponse ToResponse(ProviderFlightDeparture departure, bool hasReservations) => new()
    {
        Id = departure.Id,
        ProviderFlightId = departure.ProviderFlightId,
        DepartureAt = departure.DepartureAt,
        ArrivalAt = departure.ArrivalAt,
        TotalSeats = departure.TotalSeats,
        AvailableSeats = departure.AvailableSeats,
        AdultPrice = departure.AdultPrice,
        ChildPrice = departure.ChildPrice,
        InfantPrice = departure.InfantPrice,
        Currency = departure.Currency,
        Status = departure.Status.ToString(),
        HasReservations = hasReservations,
    };
}
