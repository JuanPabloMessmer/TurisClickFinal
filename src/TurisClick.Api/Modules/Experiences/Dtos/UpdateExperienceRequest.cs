using System.ComponentModel.DataAnnotations;
using TurisClick.Api.Shared.Validation;

namespace TurisClick.Api.Modules.Experiences.Dtos;

/// <summary>
/// UC-P-05 — Editar experiencia. Mismos campos editables que la creación (todo el contenido de
/// catálogo); Status se controla aparte vía Publish/Unpublish (UC-P-06), no acá.
/// </summary>
public class UpdateExperienceRequest
{
    [Required, MinLength(3), MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required, MinLength(10)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public Guid DestinationId { get; set; }

    public List<Guid> CategoryIds { get; set; } = [];

    public string? IncludesText { get; set; }
    public string? ExcludesText { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "DurationMinutes debe ser mayor a 0.")]
    public int? DurationMinutes { get; set; }

    [MaxLength(100)]
    public string? DurationLabel { get; set; }

    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }

    [Required, Iso4217Currency]
    public string Currency { get; set; } = string.Empty;

    public List<ExperienceImageRequest> Images { get; set; } = [];
}
