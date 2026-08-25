using TurisClick.Api.Modules.Companies.Entities;
using TurisClick.Api.Modules.Experiences.Entities;

namespace TurisClick.Api.Modules.Reservations.Entities;

/// <summary>
/// Una línea reservable de un solo proveedor (decisión 2). En Oleada 2 siempre ProductType = EXPERIENCE;
/// PackageId/PackageAvailabilityId ya existen en el modelo (sin FK física, la tabla packages no existe
/// hasta Oleada 4) para no tener que romper este modelo cuando se agregue esa oleada.
/// </summary>
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

    /// <summary>Sin FK física todavía — la tabla packages no existe hasta Oleada 4.</summary>
    public Guid? PackageId { get; set; }

    public Guid? ExperienceAvailabilityId { get; set; }
    public ExperienceAvailability? ExperienceAvailability { get; set; }

    /// <summary>Sin FK física todavía — la tabla package_availabilities no existe hasta Oleada 4.</summary>
    public Guid? PackageAvailabilityId { get; set; }

    public int Travelers { get; set; }

    /// <summary>Precio congelado al momento de reservar (snapshot) — nunca se recalcula desde el precio vigente del producto.</summary>
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }

    public ReservationItemStatus Status { get; set; } = ReservationItemStatus.PENDING_PAYMENT;

    /// <summary>Solo tiene sentido si Reservation.AiItineraryId no es nulo (orden del viaje) — Oleada 2 lo deja nulo.</summary>
    public int? DayNumber { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
