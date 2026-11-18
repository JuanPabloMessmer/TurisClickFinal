using System.ComponentModel.DataAnnotations;
using TurisClick.Api.Shared.Validation;

namespace TurisClick.Api.Modules.Categories.Dtos;

/// <summary>UC-A-05 — Gestionar categorías (alta). Reglas R-07 y R-08 de docs/auditoria-validaciones.md.</summary>
public class CreateCategoryRequest
{
    [Required(AllowEmptyStrings = true)] // obligatorio en el contrato OpenAPI; el mensaje lo da la regla propia
    [Rule.CategoryName]
    public string Name { get; set; } = string.Empty;

    [Rule.PlainText("La descripción", ValidationLimits.CategoryDescriptionMax)]
    public string? Description { get; set; }
}
