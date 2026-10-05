using TurisClick.Api.Modules.Flights.Services;
using TurisClick.Api.Modules.Packages.Entities;
using TurisClick.Api.Modules.Reservations.Entities;

namespace TurisClick.Api.Modules.Flights.Entities;

/// <summary>
/// Cómo se busca el vuelo de un paquete. El operador **no carga un vuelo**: carga la regla con la que
/// TurisClick lo busca cada vez. Por eso acá no hay aerolínea, ni número de vuelo, ni tarifa: esos datos
/// salen del proveedor en el momento de cotizar y nunca se escriben a mano.
///
/// 1–1 con Package (la PK es el package_id), igual que el resto del módulo: existe si y sólo si el
/// paquete declara que incluye vuelo.
/// </summary>
public class PackageFlightRule
{
    public Guid PackageId { get; set; }
    public Package? Package { get; set; }

    /// <summary>Aeropuerto al que vuela el paquete (IATA, mayúsculas).</summary>
    public string DestinationIata { get; set; } = string.Empty;

    /// <summary>
    /// Orígenes que el operador acepta, separados por coma. Es una lista corta y cerrada —"desde Santa
    /// Cruz o desde La Paz"—, no un catálogo: una columna de texto evita una tabla entera para dos o
    /// tres códigos.
    /// </summary>
    public string AllowedOriginIatas { get; set; } = string.Empty;

    public FlightCabinClass CabinClass { get; set; } = FlightCabinClass.ECONOMY;

    /// <summary>
    /// Días entre la salida del paquete y el vuelo de ida. 0 = el mismo día; -1 = la noche anterior,
    /// que es lo habitual cuando el vuelo llega de madrugada.
    /// </summary>
    public int OutboundOffsetDays { get; set; }

    /// <summary>
    /// Días entre el ÚLTIMO día del paquete y el vuelo de vuelta. 0 = el mismo día que termina.
    /// Sólo se usa si el viaje es de ida y vuelta.
    /// </summary>
    public int InboundOffsetDays { get; set; }

    public bool RoundTrip { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public IReadOnlyList<string> Origins() =>
        AllowedOriginIatas.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>
/// Una cotización concreta que TurisClick le mostró a alguien. Se persiste por una razón puntual: para
/// revalidar hay que comparar contra el precio que se mostró, y ese precio **no puede venir del
/// cliente** — si viniera, cualquiera podría declarar que le cotizaron más barato.
///
/// Es deliberadamente volátil: guarda el identificador opaco de la oferta del proveedor y los datos que
/// se le mostraron a la persona. No guarda la respuesta cruda del proveedor ni un solo dato de pasajero.
/// </summary>
public class FlightQuote
{
    public Guid Id { get; set; }

    public Guid PackageId { get; set; }
    public Package? Package { get; set; }

    /// <summary>Salida del paquete para la que se cotizó; ancla el vuelo a una fecha real del catálogo.</summary>
    public Guid PackageAvailabilityId { get; set; }
    public PackageAvailability? PackageAvailability { get; set; }

    /// <summary>Quién cotizó, si había sesión. El catálogo es público, así que puede ser nulo.</summary>
    public Guid? TouristId { get; set; }

    public string Provider { get; set; } = string.Empty;

    /// <summary>Identificador opaco de la oferta en el proveedor: lo único que hace falta para revalidar y reservar.</summary>
    public string ProviderOfferId { get; set; } = string.Empty;

    public string OriginIata { get; set; } = string.Empty;
    public string DestinationIata { get; set; } = string.Empty;

    public DateOnly OutboundDate { get; set; }
    public DateOnly? InboundDate { get; set; }

    public int Travelers { get; set; }

    /// <summary>Precio total de la oferta para todos los pasajeros, tal como lo devolvió el proveedor.</summary>
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = string.Empty;

    /// <summary>Precio con el que se cotizó originalmente. Si una revalidación lo cambia, este queda como estaba.</summary>
    public decimal InitialAmount { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset QuotedAt { get; set; }
    public DateTimeOffset? RevalidatedAt { get; set; }

    public FlightQuoteStatus Status { get; set; } = FlightQuoteStatus.QUOTED;

    /// <summary>Resumen legible del itinerario (aerolínea, horarios, escalas) para no re-consultar al proveedor al listar.</summary>
    public string ItinerarySummary { get; set; } = string.Empty;

    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } expiry && expiry <= now;
}

public enum FlightQuoteStatus
{
    /// <summary>Recién cotizada; todavía no se volvió a preguntar al proveedor.</summary>
    QUOTED,

    /// <summary>Revalidada y vigente con el mismo precio.</summary>
    CONFIRMED,

    /// <summary>Revalidada y el proveedor devolvió otro precio.</summary>
    PRICE_CHANGED,

    /// <summary>Venció por tiempo.</summary>
    EXPIRED,

    /// <summary>El proveedor ya no la ofrece.</summary>
    UNAVAILABLE,
}

/// <summary>
/// La reserva aérea, cuando exista. **1–1 con Reservation y no un ReservationItem**, y la razón es del
/// modelo, no estética: ReservationItem exige CompanyId (aislamiento por empresa, UC-SYS-03) y una
/// availability propia por su restricción de forma; un vuelo no tiene ninguna de las dos, y meterlo ahí
/// obligaría a debilitar las dos garantías y a mostrarle vuelos ajenos a cada operador en su listado.
///
/// En esta oleada la tabla existe y nada la escribe todavía: la orquestación de reserva es la próxima.
/// </summary>
public class FlightBooking
{
    public Guid Id { get; set; }

    public Guid ReservationId { get; set; }
    public Reservation? Reservation { get; set; }

    public Guid FlightQuoteId { get; set; }
    public FlightQuote? FlightQuote { get; set; }

    public string Provider { get; set; } = string.Empty;
    public string? ProviderOrderId { get; set; }

    /// <summary>Localizador de la reserva: lo que el pasajero necesita para presentarse.</summary>
    public string? BookingReference { get; set; }

    public FlightBookingStatus Status { get; set; } = FlightBookingStatus.PENDING;

    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Clave propia escrita ANTES de llamar al proveedor. Es lo que hace recuperable el peor caso: si el
    /// proveedor crea la orden y nuestra escritura falla, queda rastro para reconciliar en vez de una
    /// reserva huérfana que nadie puede encontrar.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public string? FailureReason { get; set; }
}

public enum FlightBookingStatus
{
    PENDING,
    CONFIRMED,
    FAILED,
    CANCELLED,
}
