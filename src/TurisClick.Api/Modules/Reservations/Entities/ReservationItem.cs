using TurisClick.Api.Modules.Companies.Entities;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Flights.ProviderFlights.Entities;
using TurisClick.Api.Modules.Packages.Entities;

namespace TurisClick.Api.Modules.Reservations.Entities;

/// <summary>Una línea reservable de un solo proveedor (decisión 2). ProductType = EXPERIENCE, PACKAGE (Oleada 4) o FLIGHT (Oleada 18).</summary>
public class ReservationItem
{
    public Guid Id { get; set; }

    public Guid ReservationId { get; set; }
    public Reservation? Reservation { get; set; }

    /// <summary>Denormalizado a propósito (UC-P-12/13, UC-SYS-03) — evita un join hasta Experience/Package en cada consulta del Provider.</summary>
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public ProductType ProductType { get; set; }

    public Guid? ExperienceId { get; set; }
    public Experience? Experience { get; set; }

    public Guid? PackageId { get; set; }
    public Package? Package { get; set; }

    public Guid? ExperienceAvailabilityId { get; set; }
    public ExperienceAvailability? ExperienceAvailability { get; set; }

    public Guid? PackageAvailabilityId { get; set; }
    public PackageAvailability? PackageAvailability { get; set; }

    /// <summary>Vuelo de operador (ProductType FLIGHT): el vuelo y la salida concreta. Una salida por ítem; ida y vuelta son dos ítems.</summary>
    public Guid? ProviderFlightId { get; set; }
    public ProviderFlight? ProviderFlight { get; set; }

    public Guid? ProviderFlightDepartureId { get; set; }
    public ProviderFlightDeparture? ProviderFlightDeparture { get; set; }

    public int Travelers { get; set; }

    /// <summary>Precio congelado al momento de reservar (snapshot) — nunca se recalcula desde el precio vigente del producto.</summary>
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }

    public ReservationItemStatus Status { get; set; } = ReservationItemStatus.PENDING_PAYMENT;

    /// <summary>Cuándo dejó de estar activa esta línea (cancelación o expiración). Nulo mientras siga vigente.</summary>
    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>
    /// Motivo que dejó el proveedor al cancelar su línea (UC-P-14, cancelación excepcional por fuerza
    /// mayor). Nulo en el resto de los casos — quién canceló es derivable: si la Reservation padre
    /// también tiene CancelledAt fue el turista sobre todo el viaje; si solo lo tiene la línea, fue el
    /// proveedor sobre su parte.
    /// </summary>
    public string? CancellationReason { get; set; }

    /// <summary>Solo tiene sentido si Reservation.AiItineraryId no es nulo (orden del viaje) — todavía nulo hasta Oleada 5+.</summary>
    public int? DayNumber { get; set; }

    /// <summary>
    /// **Copia** de la política de cancelación del producto al momento de reservar (ver
    /// `CancellationPolicy`). Es un snapshot, igual que `UnitPrice`: si el operador cambia su política
    /// mañana, esta reserva conserva la que la persona aceptó. Nula = no había política y esta línea no se
    /// puede cancelar una vez confirmada.
    /// </summary>
    public string? CancellationPolicy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // ------------------------------------------------------------------ emisión (vuelos de operador)
    //
    // El operador emite el pasaje fuera de TurisClick y lo registra. Hasta entonces nada en la app dice
    // "pasaje emitido". NOT_REQUIRED en cualquier ítem que no sea un vuelo, y en un vuelo todavía sin pagar.

    public FlightIssuanceStatus IssuanceStatus { get; set; } = FlightIssuanceStatus.NOT_REQUIRED;

    /// <summary>Un número por pasajero, en el orden de <see cref="Passengers"/>, separados por coma.</summary>
    public string? TicketNumbers { get; set; }

    public string? BookingReference { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public Guid? IssuedByUserId { get; set; }
    public string? IssuanceNote { get; set; }

    public ICollection<ReservationPassenger> Passengers { get; set; } = new List<ReservationPassenger>();
}

public enum FlightIssuanceStatus
{
    /// <summary>No hay emisión en curso: ítems que no son vuelos de operador, o vuelos todavía sin pagar.</summary>
    NOT_REQUIRED,

    /// <summary>Pagado: el operador tiene que emitir y registrarlo.</summary>
    PENDING,

    /// <summary>El operador registró la emisión con los números de pasaje.</summary>
    ISSUED,

    /// <summary>El operador no pudo emitir: el ítem se cancela con reembolso total.</summary>
    FAILED,
}
