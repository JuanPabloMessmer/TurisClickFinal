using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Reservations.Dtos;

/// <summary>UC-T-08 — Reservar una experiencia individual.</summary>
public class CreateReservationRequest
{
    [Required]
    public Guid ExperienceAvailabilityId { get; set; }

    [Range(1, 100)]
    public int Travelers { get; set; }
}
