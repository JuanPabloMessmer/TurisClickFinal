using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Flights.Dtos;
using TurisClick.Api.Modules.Flights.Entities;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Flights.Services;

/// <summary>
/// La parte aérea de una reserva coordinada. Existe porque PostgreSQL y el proveedor de vuelos **no
/// comparten transacción**, y fingir que sí es la forma más rápida de vender un pasaje que no existe o de
/// cobrar dos veces el mismo.
///
/// El reparto de responsabilidades con Reservations es deliberado:
/// este servicio decide **qué pasó con el proveedor** y escribe el FlightBooking; Reservations decide
/// **qué pasa con la reserva y el cupo**. Así ninguno de los dos módulos termina reimplementando al otro,
/// y la compensación (liberar cupo, cancelar la reserva) sigue viviendo donde vive el cupo.
///
/// Las tres etapas se llaman en orden desde el pago y están separadas por una razón concreta:
///
/// <list type="number">
/// <item><see cref="PrepareAsync"/> — valida la cotización y arma la intención. Nada irreversible.</item>
/// <item><see cref="PreflightAsync"/> — revalida contra el proveedor ANTES de cobrar. Acá se decide si
/// hace falta que la persona acepte un precio nuevo.</item>
/// <item><see cref="BeginOrderingAsync"/> + <see cref="PlaceOrderAsync"/> — recién después del cobro se
/// emite. El estado ORDERING se commitea antes de la llamada: es la única forma de que una caída del
/// proceso a mitad de camino deje rastro en vez de una compra fantasma.</item>
/// </list>
/// </summary>
public interface IFlightBookingOrchestrator
{
    /// <summary>
    /// Idempotencia de la creación: si esta cotización ya produjo una reserva, devuelve su id en vez de
    /// dejar que se retenga cupo por segunda vez. Es lo que vuelve inofensivos el doble toque y el
    /// reintento del cliente tras un timeout.
    /// </summary>
    Task<Guid?> FindReservationForQuoteAsync(Guid quoteId, Guid touristId, CancellationToken ct);

    /// <summary>
    /// Valida la cotización contra la reserva que se está armando y devuelve el FlightBooking **sin
    /// persistir**: lo guarda el caller, dentro de la misma transacción en la que toma el cupo.
    /// </summary>
    Task<FlightBooking> PrepareAsync(
        Guid quoteId, Guid touristId, Guid packageAvailabilityId, int travelers, CancellationToken ct);

    /// <summary>Revalida la oferta contra el proveedor. No cobra, no emite y no persiste estados finales.</summary>
    Task<FlightPreflight> PreflightAsync(FlightBooking booking, MoneyRequest? acceptedPrice, CancellationToken ct);

    /// <summary>
    /// Toma la transición PENDING → ORDERING y la commitea. Devuelve false si otra ejecución la ganó: es
    /// el candado que impide que dos pagos simultáneos emitan dos pasajes.
    /// </summary>
    Task<bool> BeginOrderingAsync(FlightBooking booking, CancellationToken ct);

    /// <summary>Emite contra el proveedor. No escribe en la base: el resultado se aplica con <see cref="Apply"/>.</summary>
    Task<FlightOrderOutcome> PlaceOrderAsync(
        FlightBooking booking, FlightOffer offer, IReadOnlyList<FlightTravelerRequest> travelers, CancellationToken ct);

    /// <summary>
    /// Vuelca el desenlace sobre la entidad (sin guardar) para que el caller lo persista en la MISMA
    /// transacción en la que confirma o compensa la reserva. Una sola escritura local después de la
    /// llamada externa: así no queda un vuelo confirmado con una reserva sin confirmar.
    /// </summary>
    void Apply(FlightBooking booking, FlightOrderOutcome outcome, DateTimeOffset now);

    FlightBookingResponse ToResponse(FlightBooking booking);
}

public enum FlightPreflightKind
{
    /// <summary>El precio vigente es el que la persona ya aceptó: se puede cobrar y emitir.</summary>
    READY,

    /// <summary>Cambió el precio. No se cobra nada hasta que acepte explícitamente el importe nuevo.</summary>
    REQUIRES_ACCEPTANCE,

    /// <summary>La oferta ya no existe o venció: hay que volver a buscar.</summary>
    UNAVAILABLE,

    /// <summary>No se pudo consultar al proveedor. Nada cambió; se puede reintentar.</summary>
    PROVIDER_UNAVAILABLE,

    /// <summary>La oferta exige datos que todavía no pedimos. No se vende antes que pedir datos de más.</summary>
    UNSUPPORTED,
}

public record FlightPreflight(
    FlightPreflightKind Kind,
    /// <summary>La oferta revalidada. Es la que se usa para emitir: el precio que se manda es este, no uno del cliente.</summary>
    FlightOffer? Offer,
    MoneyResponse? Previous,
    MoneyResponse? Current,
    string Message,
    string? ErrorCode = null);

public enum FlightOrderOutcomeKind
{
    CONFIRMED,

    /// <summary>Rechazo definitivo del proveedor. No existe ninguna orden; corresponde liberar el cupo.</summary>
    FAILED,

    /// <summary>La solicitud nunca salió. No hay orden del otro lado y se puede reintentar sin riesgo.</summary>
    NOT_SENT,

    /// <summary>Desenlace desconocido. Prohibido reintentar: primero hay que preguntarle al proveedor.</summary>
    RECONCILING,
}

public record FlightOrderOutcome(
    FlightOrderOutcomeKind Kind,
    FlightOrderResult? Order,
    string Message,
    string? ErrorCode = null);

public class FlightBookingOrchestrator(
    TurisClickDbContext db,
    IFlightProvider flightProvider,
    ILogger<FlightBookingOrchestrator> logger) : IFlightBookingOrchestrator
{
    public async Task<Guid?> FindReservationForQuoteAsync(Guid quoteId, Guid touristId, CancellationToken ct)
    {
        var existing = await db.FlightBookings
            .AsNoTracking()
            .Include(b => b.Reservation)
            .FirstOrDefaultAsync(b => b.FlightQuoteId == quoteId, ct);

        if (existing is null) return null;

        // Una cotización ajena no se reutiliza ni se delata: para quien no es su dueño, no existe.
        if (existing.Reservation?.TouristId != touristId)
            throw new NotFoundAppException("Cotización no encontrada.", ErrorCodes.FlightQuoteMismatch);

        return existing.ReservationId;
    }

    public async Task<FlightBooking> PrepareAsync(
        Guid quoteId, Guid touristId, Guid packageAvailabilityId, int travelers, CancellationToken ct)
    {
        var quote = await db.FlightQuotes.FirstOrDefaultAsync(q => q.Id == quoteId, ct)
            ?? throw new NotFoundAppException("Cotización no encontrada.", ErrorCodes.FlightQuoteMismatch);

        // Una cotización pertenece a quien la pidió. Si se dejara reservar la de otro, el precio de una
        // persona podría comprarlo cualquiera.
        if (quote.TouristId is { } owner && owner != touristId)
            throw new NotFoundAppException("Cotización no encontrada.", ErrorCodes.FlightQuoteMismatch);

        // La cotización se hizo para UNA salida y UNA cantidad de viajeros: las fechas y el precio del
        // pasaje dependen de las dos. Aceptar otra combinación sería vender un vuelo de otro viaje.
        if (quote.PackageAvailabilityId != packageAvailabilityId || quote.Travelers != travelers)
            throw new ValidationAppException(
                "La cotización del vuelo no corresponde a esta salida o a esta cantidad de viajeros.",
                ErrorCodes.FlightQuoteMismatch);

        if (quote.Status is FlightQuoteStatus.UNAVAILABLE)
            throw new ConflictAppException(
                "Ese vuelo ya no está disponible. Buscá de nuevo para ver las opciones vigentes.",
                ErrorCodes.FlightUnavailable);

        if (quote.IsExpired(DateTimeOffset.UtcNow) || quote.Status is FlightQuoteStatus.EXPIRED)
            throw new ConflictAppException(
                "La cotización del vuelo venció. Buscá vuelos de nuevo para ver el precio actual.",
                ErrorCodes.FlightQuoteExpired);

        // El catálogo permite cotizar sin haber decidido nada; al reservar ya hay un dueño y queda escrito.
        quote.TouristId = touristId;

        var now = DateTimeOffset.UtcNow;

        return new FlightBooking
        {
            Id = Guid.NewGuid(),
            FlightQuoteId = quote.Id,
            Provider = quote.Provider,
            Status = FlightBookingStatus.PENDING,
            TotalAmount = quote.TotalAmount,
            Currency = quote.Currency,
            // Clave propia, escrita antes de cualquier llamada externa. Viaja como metadato de la orden:
            // es lo que permite reconocerla si se pierde la respuesta.
            IdempotencyKey = $"tc_{quote.Id:N}",
            CreatedAt = now,
            // Snapshot de ruta y fechas desde ya: la cotización vence, y lo que se compró no puede
            // depender de una fila que caduca.
            OriginIata = quote.OriginIata,
            DestinationIata = quote.DestinationIata,
            OutboundDate = quote.OutboundDate,
            InboundDate = quote.InboundDate,
            Travelers = quote.Travelers,
            ItinerarySummary = quote.ItinerarySummary,
        };
    }

    public async Task<FlightPreflight> PreflightAsync(
        FlightBooking booking, MoneyRequest? acceptedPrice, CancellationToken ct)
    {
        var quote = booking.FlightQuote
            ?? await db.FlightQuotes.FirstOrDefaultAsync(q => q.Id == booking.FlightQuoteId, ct)
            ?? throw new InvalidOperationException("El vuelo de la reserva no tiene cotización asociada.");

        var previous = new MoneyResponse { Amount = booking.TotalAmount, Currency = booking.Currency };

        if (quote.IsExpired(DateTimeOffset.UtcNow))
            return new FlightPreflight(
                FlightPreflightKind.UNAVAILABLE, null, previous, null,
                "La cotización del vuelo venció. Buscá vuelos de nuevo para ver el precio actual.",
                ErrorCodes.FlightQuoteExpired);

        FlightOffer offer;
        try
        {
            offer = await flightProvider.RefreshOfferAsync(quote.ProviderOfferId, ct);
        }
        catch (FlightOfferExpiredException)
        {
            quote.Status = FlightQuoteStatus.UNAVAILABLE;
            quote.RevalidatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            return new FlightPreflight(
                FlightPreflightKind.UNAVAILABLE, null, previous, null,
                "Ese vuelo ya no está disponible. Buscá de nuevo para ver las opciones vigentes.",
                ErrorCodes.FlightUnavailable);
        }
        catch (FlightProviderException ex)
        {
            // No se sabe el precio vigente, así que no se cobra: cobrar a ciegas es exactamente lo que
            // este paso existe para evitar.
            logger.LogWarning(ex, "No se pudo revalidar el vuelo de la reserva {ReservationId}.", booking.ReservationId);

            return new FlightPreflight(
                FlightPreflightKind.PROVIDER_UNAVAILABLE, null, previous, null,
                "No pudimos confirmar el precio del vuelo en este momento. Probá de nuevo en unos minutos.",
                ErrorCodes.FlightProviderUnreachable);
        }

        if (offer.IdentityDocumentsRequired)
            return new FlightPreflight(
                FlightPreflightKind.UNSUPPORTED, null, previous, null,
                "Este vuelo exige documento de identidad de cada pasajero y todavía no lo pedimos en la app. " +
                "Elegí otra opción de vuelo.",
                ErrorCodes.FlightRequirementsUnsupported);

        quote.RevalidatedAt = DateTimeOffset.UtcNow;
        quote.ExpiresAt = offer.ExpiresAt;

        var current = new MoneyResponse { Amount = offer.Price.Amount, Currency = offer.Price.Currency };
        var changed = offer.Price.DiffersFrom(new FlightPrice(booking.TotalAmount, booking.Currency));

        if (changed)
        {
            quote.TotalAmount = offer.Price.Amount;
            quote.Currency = offer.Price.Currency;
            quote.Status = FlightQuoteStatus.PRICE_CHANGED;

            // La aceptación se ata al importe REVALIDADO, no a un booleano: un "sí" dado sobre 450 no
            // sirve para cobrar 470. Si el precio se mueve otra vez, se vuelve a preguntar.
            var accepts = acceptedPrice is not null
                && acceptedPrice.Amount == offer.Price.Amount
                && string.Equals(acceptedPrice.Currency, offer.Price.Currency, StringComparison.OrdinalIgnoreCase);

            if (!accepts)
            {
                await db.SaveChangesAsync(ct);

                logger.LogInformation(
                    "Vuelo de la reserva {ReservationId}: el precio cambió y falta aceptación explícita.",
                    booking.ReservationId);

                return new FlightPreflight(
                    FlightPreflightKind.REQUIRES_ACCEPTANCE, offer, previous, current,
                    offer.Price.Amount > previous.Amount
                        ? "El precio del vuelo subió. Revisá el nuevo total antes de confirmar."
                        : "El precio del vuelo bajó. Confirmá el nuevo total para continuar.",
                    ErrorCodes.FlightPriceChanged);
            }

            // Aceptado: el importe que se va a cobrar y a emitir queda congelado en el booking.
            booking.TotalAmount = offer.Price.Amount;
            booking.Currency = offer.Price.Currency;
        }
        else
        {
            quote.Status = FlightQuoteStatus.CONFIRMED;
        }

        await db.SaveChangesAsync(ct);

        return new FlightPreflight(FlightPreflightKind.READY, offer, previous, current, "El precio del vuelo está confirmado.");
    }

    public async Task<bool> BeginOrderingAsync(FlightBooking booking, CancellationToken ct)
    {
        // Transición condicional: igual que el resto del sistema, la autoridad es quien gana el UPDATE.
        // Dos pagos simultáneos entran acá y sólo uno sale con permiso para emitir.
        var won = await db.FlightBookings
            .Where(b => b.Id == booking.Id && b.Status == FlightBookingStatus.PENDING)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, FlightBookingStatus.ORDERING), ct);

        if (won != 1) return false;

        // ExecuteUpdate no pasa por el change tracker, así que hay que alinear también el valor ORIGINAL de
        // la entidad. Sin esto, volver a PENDING después (la falla que no llegó a salir) parecería "sin
        // cambios" para EF y el estado quedaría en ORDERING para siempre.
        var entry = db.Entry(booking);
        entry.Property(b => b.Status).OriginalValue = FlightBookingStatus.ORDERING;
        booking.Status = FlightBookingStatus.ORDERING;

        return true;
    }

    public async Task<FlightOrderOutcome> PlaceOrderAsync(
        FlightBooking booking, FlightOffer offer, IReadOnlyList<FlightTravelerRequest> travelers, CancellationToken ct)
    {
        var passengers = MapPassengers(offer, travelers);

        try
        {
            var order = await flightProvider.CreateOrderAsync(
                // El precio que se manda es el revalidado por el backend. Si no coincidiera con el vigente
                // del proveedor, la orden se rechaza: un intento de manipular el importe termina en error,
                // nunca en una venta mal cobrada.
                new FlightOrderRequest(offer.Id, offer.Price, passengers, booking.IdempotencyKey), ct);

            logger.LogInformation(
                "Vuelo de la reserva {ReservationId} emitido: localizador {Reference} (prueba={TestMode}).",
                booking.ReservationId, order.BookingReference, !order.LiveMode);

            return new FlightOrderOutcome(FlightOrderOutcomeKind.CONFIRMED, order, "El vuelo quedó confirmado.");
        }
        catch (FlightOfferExpiredException ex)
        {
            logger.LogWarning(ex, "El vuelo de la reserva {ReservationId} dejó de estar disponible al emitir.", booking.ReservationId);

            return new FlightOrderOutcome(
                FlightOrderOutcomeKind.FAILED, null,
                "El vuelo dejó de estar disponible justo al confirmar. No se emitió nada y liberamos la salida del paquete.",
                ErrorCodes.FlightUnavailable);
        }
        catch (FlightProviderRequestException ex)
        {
            // 4xx de validación: el proveedor contestó y dijo que no. Reintentar lo mismo no cambia nada.
            // El mensaje del proveedor NO se le muestra a la persona: puede traer detalles del payload.
            logger.LogWarning(
                ex, "El proveedor rechazó la emisión de la reserva {ReservationId} (código {ProviderCode}).",
                booking.ReservationId, ex.ProviderCode);

            return new FlightOrderOutcome(
                FlightOrderOutcomeKind.FAILED, null,
                "La aerolínea rechazó la emisión del pasaje. No se cobró nada y liberamos la salida del paquete.",
                ErrorCodes.FlightBookingFailed);
        }
        catch (FlightProviderUnavailableException ex) when (!ex.RequestMayHaveBeenSent)
        {
            // La conexión nunca se abrió: del otro lado no quedó nada y reintentar es seguro.
            logger.LogWarning(ex, "No se pudo contactar al proveedor para emitir la reserva {ReservationId}.", booking.ReservationId);

            return new FlightOrderOutcome(
                FlightOrderOutcomeKind.NOT_SENT, null,
                "No pudimos contactar a la aerolínea. Tu lugar sigue reservado: probá de nuevo en unos minutos.",
                ErrorCodes.FlightProviderUnreachable);
        }
        catch (FlightProviderException ex)
        {
            // Todo lo demás es ambiguo por definición: la solicitud pudo haber llegado. Acá NO se reintenta
            // ni se libera cupo; se marca para que la reconciliación le pregunte al proveedor si la orden
            // existe. Liberar ahora podría dejar a alguien con un pasaje y sin paquete.
            logger.LogError(
                ex, "Desenlace desconocido al emitir el vuelo de la reserva {ReservationId}: queda para reconciliar.",
                booking.ReservationId);

            return new FlightOrderOutcome(
                FlightOrderOutcomeKind.RECONCILING, null,
                "Estamos confirmando tu vuelo con la aerolínea. Te avisamos en unos minutos; no hace falta volver a intentar.",
                ErrorCodes.FlightBookingInProgress);
        }
    }

    public void Apply(FlightBooking booking, FlightOrderOutcome outcome, DateTimeOffset now)
    {
        switch (outcome.Kind)
        {
            case FlightOrderOutcomeKind.CONFIRMED when outcome.Order is { } order:
                booking.Status = FlightBookingStatus.CONFIRMED;
                booking.ProviderOrderId = order.OrderId;
                booking.BookingReference = order.BookingReference;
                booking.ConfirmedAt = now;
                booking.NextReconciliationAt = null;
                // El importe que queda guardado es el que el proveedor confirmó, no el que se pidió.
                booking.TotalAmount = order.Price.Amount;
                booking.Currency = order.Price.Currency;
                ApplyItinerarySnapshot(booking, order);
                break;

            case FlightOrderOutcomeKind.FAILED:
                booking.Status = FlightBookingStatus.FAILED;
                booking.FailedAt = now;
                booking.FailureReason = Truncate(outcome.Message);
                booking.NextReconciliationAt = null;
                break;

            case FlightOrderOutcomeKind.NOT_SENT:
                // Vuelve a PENDING: nada se emitió, así que el intento puede repetirse tal cual.
                booking.Status = FlightBookingStatus.PENDING;
                booking.FailureReason = Truncate(outcome.Message);
                break;

            case FlightOrderOutcomeKind.RECONCILING:
                booking.Status = FlightBookingStatus.RECONCILIATION_REQUIRED;
                booking.FailureReason = Truncate(outcome.Message);
                booking.NextReconciliationAt = now;
                break;

            default:
                throw new InvalidOperationException($"Desenlace de emisión no contemplado: {outcome.Kind}.");
        }
    }

    /// <summary>
    /// Congela el itinerario comprado: aerolínea, números de vuelo y horarios de ida y vuelta. Es lo que
    /// permite explicar la compra meses después, cuando la oferta del proveedor ya no exista.
    /// </summary>
    internal static void ApplyItinerarySnapshot(FlightBooking booking, FlightOrderResult order)
    {
        var outbound = order.Slices.ElementAtOrDefault(0);
        var inbound = order.Slices.ElementAtOrDefault(1);

        if (outbound is not null)
        {
            var first = outbound.Segments.FirstOrDefault();
            var last = outbound.Segments.LastOrDefault();

            booking.CarrierIata ??= first?.MarketingCarrierIata;
            booking.CarrierName ??= first?.MarketingCarrierName;
            booking.OutboundDepartureAt = first?.DepartingAt;
            booking.OutboundArrivalAt = last?.ArrivingAt;
            booking.OutboundFlightNumber = first?.FlightNumber;
        }

        if (inbound is not null)
        {
            var first = inbound.Segments.FirstOrDefault();
            var last = inbound.Segments.LastOrDefault();

            booking.InboundDepartureAt = first?.DepartingAt;
            booking.InboundArrivalAt = last?.ArrivingAt;
            booking.InboundFlightNumber = first?.FlightNumber;
        }
    }

    /// <summary>
    /// Arma los pasajeros que espera la oferta. Los ids de pasajero los asignó el proveedor en la búsqueda:
    /// no son nuestros y no se inventan, así que si la oferta espera dos y llegan tres, se corta acá.
    /// </summary>
    private static List<FlightPassengerDetails> MapPassengers(
        FlightOffer offer, IReadOnlyList<FlightTravelerRequest> travelers)
    {
        if (offer.Passengers.Count != travelers.Count)
            throw new ValidationAppException(
                $"El vuelo elegido es para {offer.Passengers.Count} pasajero(s) y se enviaron {travelers.Count}.",
                ErrorCodes.FlightTravelersRequired);

        return [.. offer.Passengers.Select((slot, index) =>
        {
            var traveler = travelers[index];
            return new FlightPassengerDetails(
                slot.ProviderPassengerId,
                traveler.GivenName.Trim(),
                traveler.FamilyName.Trim(),
                traveler.BornOn,
                traveler.Gender.ToLowerInvariant(),
                traveler.Title.ToLowerInvariant(),
                traveler.Email.Trim(),
                traveler.PhoneNumber.Trim());
        })];
    }

    public FlightBookingResponse ToResponse(FlightBooking booking) => new()
    {
        Status = booking.Status.ToString(),
        OriginIata = booking.OriginIata,
        OriginLabel = AirportCatalog.Describe(booking.OriginIata),
        DestinationIata = booking.DestinationIata,
        DestinationLabel = AirportCatalog.Describe(booking.DestinationIata),
        OutboundDate = booking.OutboundDate,
        InboundDate = booking.InboundDate,
        Travelers = booking.Travelers,
        CarrierIata = booking.CarrierIata,
        CarrierName = booking.CarrierName,
        OutboundDepartureAt = booking.OutboundDepartureAt,
        OutboundArrivalAt = booking.OutboundArrivalAt,
        OutboundFlightNumber = booking.OutboundFlightNumber,
        InboundDepartureAt = booking.InboundDepartureAt,
        InboundArrivalAt = booking.InboundArrivalAt,
        InboundFlightNumber = booking.InboundFlightNumber,
        BookingReference = booking.BookingReference,
        Price = new MoneyResponse { Amount = booking.TotalAmount, Currency = booking.Currency },
        ItinerarySummary = booking.ItinerarySummary,
        StatusMessage = StatusMessage(booking.Status),
        InProgress = booking.Status is FlightBookingStatus.ORDERING or FlightBookingStatus.RECONCILIATION_REQUIRED,
    };

    /// <summary>
    /// El estado en palabras. Lo decide el dominio y no la pantalla: dos apps distintas no pueden contar
    /// historias distintas sobre el mismo vuelo.
    /// </summary>
    private static string StatusMessage(FlightBookingStatus status) => status switch
    {
        FlightBookingStatus.PENDING => "Falta confirmar el pasaje. Se emite cuando confirmás la reserva.",
        FlightBookingStatus.ORDERING => "Estamos emitiendo tu pasaje.",
        FlightBookingStatus.CONFIRMED => "Pasaje emitido.",
        FlightBookingStatus.FAILED => "No se pudo emitir el pasaje.",
        FlightBookingStatus.RECONCILIATION_REQUIRED => "Estamos confirmando tu vuelo con la aerolínea.",
        FlightBookingStatus.CANCELLED => "Pasaje cancelado.",
        _ => string.Empty,
    };

    private static string Truncate(string value) => value.Length > 500 ? value[..500] : value;
}
