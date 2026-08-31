namespace TurisClick.Api.Modules.Packages.Entities;

/// <summary>docs/domain-model.md §5 — galería propia del Package, independiente de las imágenes de sus Experience.</summary>
public class PackageImage
{
    public Guid Id { get; set; }
    public Guid PackageId { get; set; }
    public Package? Package { get; set; }

    public string Url { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsCover { get; set; }
}
