namespace TurisClick.Api.Modules.Categories.Entities;

/// <summary>docs/domain-model.md §3. Catálogo maestro administrado por ADMIN (UC-A-05).</summary>
public class Category
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
