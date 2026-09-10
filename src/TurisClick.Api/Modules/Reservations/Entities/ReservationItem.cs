using TurisClick.Api.Modules.Companies.Entities;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Packages.Entities;

namespace TurisClick.Api.Modules.Reservations.Entities;

/// <summary>Una línea reservable de un solo proveedor (decisión 2). ProductType = EXPERIENCE o PACKAGE (Oleada 4).</summary>
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

    public DateTimeOffset CreatedAt { get; set; }
}
