using TurisClick.Api.Modules.Experiences.Entities;

namespace TurisClick.Api.Modules.Packages.Entities;

/// <summary>
/// docs/domain-model.md §5. Materializa la decisión 1: día a día, un ítem referencia una Experience
/// real de la misma Company (reutilizable, vendible también por separado) o es puramente descriptivo
/// (traslado, desayuno, tiempo libre) sin producto vendible independiente.
/// </summary>
public class PackageItem
{
    public Guid Id { get; set; }

    public Guid PackageId { get; set; }
    public Package? Package { get; set; }

    /// <summary>Día del paquete al que pertenece (1..Package.DurationDays).</summary>
    public int DayNumber { get; set; }

    /// <summary>Orden dentro del día.</summary>
    public int SortOrder { get; set; }

    public PackageItemKind Kind { get; set; }

    /// <summary>Obligatorio si y solo si Kind = EXPERIENCE_REFERENCE. Debe pertenecer a la misma Company que el Package (validado en el Service, no expresable como CHECK físico).</summary>
    public Guid? ExperienceId { get; set; }
    public Experience? Experience { get; set; }

    /// <summary>Obligatorio si Kind = DESCRIPTIVE; opcional (se muestra el título de la Experience) si es EXPERIENCE_REFERENCE.</summary>
    public string? Title { get; set; }
    public string? Description { get; set; }
}
