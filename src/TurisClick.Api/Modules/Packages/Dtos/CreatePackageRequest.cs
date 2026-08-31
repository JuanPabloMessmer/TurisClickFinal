using System.ComponentModel.DataAnnotations;
using TurisClick.Api.Shared.Validation;

namespace TurisClick.Api.Modules.Packages.Dtos;

/// <summary>
/// UC-P-07 — Crear paquete. Items e Images van integrados en el mismo request (en vez de endpoints
/// fragmentados) por preferencia explícita del alcance de Oleada 4 — mismo patrón que ya usa
/// CreateExperienceRequest.Images.
/// </summary>
public class CreatePackageRequest : IValidatableObject
{
    [Required, MinLength(3), MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required, MinLength(10)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public Guid DestinationId { get; set; }

    public List<Guid> CategoryIds { get; set; } = [];

    public string? ConditionsText { get; set; }

    [Range(1, 90, ErrorMessage = "DurationDays debe estar entre 1 y 90.")]
    public int DurationDays { get; set; }

    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }

    [Required, Iso4217Currency]
    public string Currency { get; set; } = string.Empty;

    public List<PackageItemRequest> Items { get; set; } = [];

    public List<PackageImageRequest> Images { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var item in Items.Where(item => item.DayNumber > DurationDays))
            yield return new ValidationResult(
                $"El día {item.DayNumber} de un ítem excede DurationDays ({DurationDays}).", [nameof(Items)]);
    }
}
