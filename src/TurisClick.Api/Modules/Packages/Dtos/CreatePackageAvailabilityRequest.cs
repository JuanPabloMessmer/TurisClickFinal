using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Packages.Dtos;

/// <summary>UC-P-11 — una salida por request; el Provider llama varias veces para "una o varias" fechas.</summary>
public class CreatePackageAvailabilityRequest
{
    [Required]
    public DateOnly DepartureDate { get; set; }

    [Range(1, 10_000)]
    public int TotalSlots { get; set; }
}
