namespace TurisClick.Api.Shared.Exceptions;

/// <summary>
/// Código estable y legible por máquina que acompaña al error HTTP. No es un segundo sistema de
/// errores: viaja dentro del `ProblemDetails` que ya devuelve GlobalExceptionHandler, en
/// `extensions.errorCode` (RFC 7807 permite extensiones), para que un cliente pueda reaccionar sin
/// parsear el mensaje en español. Opcional: las excepciones que no lo declaran se comportan igual que antes.
/// </summary>
public interface IHasErrorCode
{
    string? ErrorCode { get; }
}

/// <summary>Códigos usados por el booking de itinerarios IA (UC-T-18) — ver docs/backend-architecture.md.</summary>
public static class ErrorCodes
{
    public const string PriceChanged = "PRICE_CHANGED";
    public const string CurrencyChanged = "CURRENCY_CHANGED";
    public const string ProductUnavailable = "PRODUCT_UNAVAILABLE";
    public const string InsufficientCapacity = "INSUFFICIENT_CAPACITY";
    public const string ItineraryAlreadyBooked = "ITINERARY_ALREADY_BOOKED";
    public const string InvalidItineraryStatus = "INVALID_ITINERARY_STATUS";
    public const string ItineraryEmpty = "ITINERARY_EMPTY";
    public const string AvailabilityNotResolved = "AVAILABILITY_NOT_RESOLVED";

    // ---- Oleada 8: expiración, cancelación y sanciones administrativas ----

    /// <summary>El pago llegó cuando la reserva ya no admitía pago (expiró o se canceló mientras se cobraba).</summary>
    public const string ReservationNoLongerPayable = "RESERVATION_NO_LONGER_PAYABLE";

    /// <summary>La reserva está en un estado que no admite cancelación (ej. ya expirada, ya cancelada).</summary>
    public const string ReservationNotCancellable = "RESERVATION_NOT_CANCELLABLE";

    /// <summary>Cancelar una reserva ya CONFIRMED requiere una política de reembolso que todavía no existe (Oleada 8: fuera de alcance).</summary>
    public const string RefundPolicyRequired = "REFUND_POLICY_REQUIRED";

    /// <summary>El contenido fue suspendido por un administrador; solo un ADMIN puede levantar la sanción.</summary>
    public const string ContentSuspended = "CONTENT_SUSPENDED";

    /// <summary>La empresa está suspendida: no puede operar comercialmente ni aparecer en el catálogo.</summary>
    public const string CompanySuspended = "COMPANY_SUSPENDED";

    // ---- Oleada 11: reserva coordinada de paquete + vuelo ----

    /// <summary>La cotización de vuelo venció: hay que buscar de nuevo, no reintentar la misma.</summary>
    public const string FlightQuoteExpired = "FLIGHT_QUOTE_EXPIRED";

    /// <summary>La cotización no corresponde al turista, al paquete o a la cantidad de viajeros de la reserva.</summary>
    public const string FlightQuoteMismatch = "FLIGHT_QUOTE_MISMATCH";

    /// <summary>El vuelo elegido ya no está disponible en el proveedor.</summary>
    public const string FlightUnavailable = "FLIGHT_UNAVAILABLE";

    /// <summary>El precio del vuelo cambió: no se cobra ni se emite nada hasta que se acepte el nuevo importe.</summary>
    public const string FlightPriceChanged = "FLIGHT_PRICE_CHANGED";

    /// <summary>La oferta exige datos que TurisClick todavía no pide (ej. documento de identidad).</summary>
    public const string FlightRequirementsUnsupported = "FLIGHT_REQUIREMENTS_UNSUPPORTED";

    /// <summary>El proveedor rechazó la emisión de forma definitiva; se liberó el cupo del paquete.</summary>
    public const string FlightBookingFailed = "FLIGHT_BOOKING_FAILED";

    /// <summary>No se pudo contactar al proveedor y la solicitud nunca salió: se puede volver a intentar.</summary>
    public const string FlightProviderUnreachable = "FLIGHT_PROVIDER_UNREACHABLE";

    /// <summary>Hay una emisión con desenlace sin resolver: no se reintenta hasta saber si existe la orden.</summary>
    public const string FlightBookingInProgress = "FLIGHT_BOOKING_IN_PROGRESS";

    /// <summary>Faltan los datos de los pasajeros, o no coinciden con la cantidad de viajeros de la reserva.</summary>
    public const string FlightTravelersRequired = "FLIGHT_TRAVELERS_REQUIRED";
}

/// <summary>Recurso solicitado no existe. El GlobalExceptionHandler la mapea a 404.</summary>
public class NotFoundAppException(string message, string? errorCode = null) : Exception(message), IHasErrorCode
{
    public string? ErrorCode { get; } = errorCode;
}

/// <summary>Conflicto de negocio (ej. email duplicado, sin cupo). Se mapea a 409.</summary>
public class ConflictAppException(string message, string? errorCode = null) : Exception(message), IHasErrorCode
{
    public string? ErrorCode { get; } = errorCode;
}

/// <summary>El usuario autenticado no tiene permiso sobre el recurso (ej. UC-SYS-03). Se mapea a 403.</summary>
public class ForbiddenAppException(string message, string? errorCode = null) : Exception(message), IHasErrorCode
{
    public string? ErrorCode { get; } = errorCode;
}

/// <summary>Credenciales inválidas o token inválido/expirado. Se mapea a 401.</summary>
public class UnauthorizedAppException(string message) : Exception(message);

/// <summary>Regla de negocio inválida que no corresponde a una validación de ModelState. Se mapea a 400.</summary>
public class ValidationAppException(string message, string? errorCode = null) : Exception(message), IHasErrorCode
{
    public string? ErrorCode { get; } = errorCode;
}

/// <summary>
/// El recurso existió pero ya no es utilizable (ej. UC-T-08: un slot de disponibilidad cerrado o con
/// fecha vencida) — distinto de NotFound porque el recurso no es inexistente, quedó obsoleto. Se mapea a 410.
/// </summary>
public class GoneAppException(string message, string? errorCode = null) : Exception(message), IHasErrorCode
{
    public string? ErrorCode { get; } = errorCode;
}
