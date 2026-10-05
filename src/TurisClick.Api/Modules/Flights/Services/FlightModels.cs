namespace TurisClick.Api.Modules.Flights.Services;

/// <summary>
/// Modelos de vuelo propios de TurisClick. Son lo único que ve el dominio: ningún tipo de Duffel (ni de
/// ningún otro proveedor) cruza esta frontera.
///
/// Decisión deliberada: los importes viajan como `decimal` + código ISO, nunca como string del
/// proveedor, y nunca se suman montos de monedas distintas — la misma regla que ya rige en reservas.
/// </summary>

public enum FlightCabinClass
{
    ECONOMY,
    PREMIUM_ECONOMY,
    BUSINESS,
    FIRST,
}

/// <summary>Un tramo pedido: ida, o vuelta. Un viaje de ida y vuelta son dos.</summary>
public record FlightSliceRequest(string OriginIata, string DestinationIata, DateOnly DepartureDate);

/// <summary>
/// Qué se busca. `Adults` se modela aparte de los menores porque es lo que todos los proveedores
/// distinguen; los infantes se suman cuando el caso de uso los pida.
/// </summary>
public record FlightSearchRequest(
    IReadOnlyList<FlightSliceRequest> Slices,
    int Adults,
    FlightCabinClass CabinClass = FlightCabinClass.ECONOMY,
    /// <summary>Techo de ofertas a traer. Una búsqueda devuelve decenas y al turista se le muestran pocas.</summary>
    int MaxOffers = 20);

public record FlightSearchResult(
    /// <summary>Id de la búsqueda en el proveedor. Se guarda para poder rastrear una oferta hasta su origen.</summary>
    string SearchId,
    IReadOnlyList<FlightOffer> Offers,
    /// <summary>Cuánto tardó el proveedor. Entra en el reporte de evidencia, no en la lógica.</summary>
    TimeSpan Elapsed);

/// <summary>
/// Una oferta concreta. `Id` es el identificador opaco del proveedor: es lo único que hace falta
/// retener entre buscar, revalidar y reservar — no se guarda la respuesta cruda entera.
/// </summary>
public record FlightOffer(
    string Id,
    FlightPrice Price,
    IReadOnlyList<FlightSlice> Slices,
    /// <summary>Cuándo deja de ser válida. Duffel la da explícita; otros proveedores pueden no hacerlo.</summary>
    DateTimeOffset? ExpiresAt,
    /// <summary>Si el proveedor exige documento de identidad de cada pasajero para reservar.</summary>
    bool IdentityDocumentsRequired,
    /// <summary>Si la reserva exige pago inmediato (si no, la oferta admite mantenerse sin pagar).</summary>
    bool InstantPaymentRequired,
    /// <summary>false = la oferta vino de un entorno de prueba. Se propaga para que nadie confunda una cosa con la otra.</summary>
    bool LiveMode,
    string? OwnerName,
    string? OwnerIataCode,
    /// <summary>Un lugar por pasajero, con el id que el proveedor asignó en la búsqueda: la orden los exige.</summary>
    IReadOnlyList<FlightPassengerSlot> Passengers)
{
    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } expiry && expiry <= now;
}

public record FlightPrice(decimal Amount, string Currency)
{
    public bool DiffersFrom(FlightPrice other) => Amount != other.Amount || Currency != other.Currency;
}

/// <summary>Un trayecto de origen a destino; puede tener escalas, y entonces tiene varios segmentos.</summary>
public record FlightSlice(
    string OriginIata,
    string DestinationIata,
    TimeSpan? Duration,
    IReadOnlyList<FlightSegment> Segments);

/// <summary>
/// Un vuelo concreto. Los horarios son `DateTime` sin huso **a propósito**: las aerolíneas los publican
/// en hora local del aeropuerto y sin offset, así que convertirlos a UTC con el huso del servidor
/// correría todos los vuelos tantas horas como esté desfasado el servidor. "Sale 13:56 de Viru Viru"
/// significa 13:56 en Viru Viru, se mire desde donde se mire.
/// </summary>
public record FlightSegment(
    string OriginIata,
    string DestinationIata,
    DateTime DepartingAt,
    DateTime ArrivingAt,
    string? MarketingCarrierIata,
    string? MarketingCarrierName,
    string? FlightNumber,
    string? Aircraft,
    /// <summary>Equipaje incluido, tal como lo informa el proveedor. Si no lo informa, queda nulo y no se inventa.</summary>
    int? CheckedBags);

/// <summary>
/// Qué datos hay que pedirle a cada pasajero. Sale de la oferta, no de una lista fija nuestra: pedir el
/// pasaporte "por las dudas" es recolectar datos sensibles que quizá nadie necesita.
/// </summary>
public record FlightPassengerRequirements(
    bool IdentityDocumentsRequired,
    IReadOnlyList<FlightPassengerSlot> Passengers);

/// <summary>Cada pasajero que la oferta espera, con el id que el proveedor le asignó en la búsqueda.</summary>
public record FlightPassengerSlot(string ProviderPassengerId, string Type);

public record FlightPassengerDetails(
    string ProviderPassengerId,
    string GivenName,
    string FamilyName,
    DateOnly BornOn,
    /// <summary>"m" / "f" según lo que acepta el proveedor; se mapea en el adapter.</summary>
    string Gender,
    string Title,
    string Email,
    string PhoneNumber);

public record FlightOrderRequest(
    string OfferId,
    /// <summary>Monto que el backend revalidó. El proveedor rechaza la orden si no coincide con su precio vigente.</summary>
    FlightPrice ConfirmedPrice,
    IReadOnlyList<FlightPassengerDetails> Passengers);

public record FlightOrderResult(
    string OrderId,
    /// <summary>Localizador de la reserva: lo que el pasajero necesita para presentarse.</summary>
    string? BookingReference,
    FlightPrice Price,
    bool LiveMode,
    DateTimeOffset? CreatedAt,
    IReadOnlyList<FlightSlice> Slices);

public record FlightCancellationResult(
    string CancellationId,
    decimal? RefundAmount,
    string? RefundCurrency,
    string? RefundTo,
    DateTimeOffset? ConfirmedAt);
