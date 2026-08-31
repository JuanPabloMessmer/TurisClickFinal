using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Reservations.Dtos;

/// <summary>
/// UC-T-08/UC-T-09 — Reservar una Experience individual o un Package de proveedor. Exactamente uno de
/// ExperienceAvailabilityId/PackageAvailabilityId debe venir presente (mismo invariante de forma que
/// ReservationItem/database-design.md ck_reservation_items_product_shape).
/// </summary>
public class CreateReservationRequest : IValidatableObject
{
    /// <summary>UC-T-08. Exactamente uno de este o PackageAvailabilityId.</summary>
    public Guid? ExperienceAvailabilityId { get; set; }

    /// <summary>UC-T-09. Exactamente uno de este o ExperienceAvailabilityId.</summary>
    public Guid? PackageAvailabilityId { get; set; }

    [Range(1, 100)]
    public int Travelers { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var hasExperience = ExperienceAvailabilityId.HasValue;
        var hasPackage = PackageAvailabilityId.HasValue;

        if (hasExperience == hasPackage)
            yield return new ValidationResult(
                "Debe indicarse exactamente uno de ExperienceAvailabilityId o PackageAvailabilityId.",
                [nameof(ExperienceAvailabilityId), nameof(PackageAvailabilityId)]);
    }
}
