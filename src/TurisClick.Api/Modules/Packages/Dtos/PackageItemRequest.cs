using System.ComponentModel.DataAnnotations;
using TurisClick.Api.Modules.Packages.Entities;

namespace TurisClick.Api.Modules.Packages.Dtos;

/// <summary>
/// Un día del itinerario del paquete: referencia una Experience real de la misma Company, o es
/// puramente descriptivo (traslado, desayuno, tiempo libre). La pertenencia de ExperienceId a la
/// misma Company del Package es una regla de negocio (se valida en el Service, no acá) — esta clase
/// solo valida la FORMA del ítem (docs/backend-architecture.md §9, mismo invariante que
/// ck_package_items_kind_shape en database-design.md). Kind es string (no el enum directo) por el mismo
/// motivo que Destination.Type en CreateDestinationRequest: no hay un JsonStringEnumConverter global
/// registrado, así que el binding de un enum C# desde un string JSON fallaría con 400.
/// </summary>
public class PackageItemRequest : IValidatableObject
{
    [Range(1, int.MaxValue, ErrorMessage = "DayNumber debe ser mayor o igual a 1.")]
    public int DayNumber { get; set; }

    public int SortOrder { get; set; }

    [Required, EnumDataType(typeof(PackageItemKind))]
    public string Kind { get; set; } = string.Empty;

    public Guid? ExperienceId { get; set; }

    [MaxLength(200)]
    public string? Title { get; set; }

    public string? Description { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enum.TryParse<PackageItemKind>(Kind, out var kind))
            yield break; // [EnumDataType] ya reporta el error de forma; evita un segundo mensaje redundante.

        if (kind == PackageItemKind.EXPERIENCE_REFERENCE && ExperienceId is null)
            yield return new ValidationResult(
                "ExperienceId es obligatorio cuando Kind es EXPERIENCE_REFERENCE.", [nameof(ExperienceId)]);

        if (kind == PackageItemKind.DESCRIPTIVE)
        {
            if (ExperienceId is not null)
                yield return new ValidationResult(
                    "ExperienceId debe quedar vacío cuando Kind es DESCRIPTIVE.", [nameof(ExperienceId)]);

            if (string.IsNullOrWhiteSpace(Title))
                yield return new ValidationResult(
                    "Title es obligatorio cuando Kind es DESCRIPTIVE.", [nameof(Title)]);
        }
    }
}
