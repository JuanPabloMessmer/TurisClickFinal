using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Experiences.Dtos;

/// <summary>UC-P-10 — un slot por request; el Provider llama varias veces para "uno o varios" slots.</summary>
public class CreateExperienceAvailabilityRequest
{
    [Required]
    public DateOnly Date { get; set; }

    /// <summary>Nulo = disponibilidad de día completo.</summary>
    public TimeOnly? StartTime { get; set; }

    [Range(1, 10_000)]
    public int TotalSlots { get; set; }
}
