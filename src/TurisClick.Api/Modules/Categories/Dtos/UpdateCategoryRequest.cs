using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Categories.Dtos;

/// <summary>UC-A-05 — Gestionar categorías (edición).</summary>
public class UpdateCategoryRequest
{
    [Required, MinLength(2), MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }
}
