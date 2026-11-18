using System.ComponentModel.DataAnnotations;
using TurisClick.Api.Shared.Validation;

namespace TurisClick.Api.Modules.Companies.Dtos;

/// <summary>
/// UC-P-02 — Gestionar perfil de "Mi Empresa". Solo datos públicos/de contacto: LegalDocument no es
/// editable por el propio proveedor (identificador legal fijado al registrarse) y Status lo controla
/// únicamente el ADMIN (UC-A-02/03). Reglas R-01, R-02, R-04 y R-05 de docs/auditoria-validaciones.md.
/// </summary>
public class UpdateCompanyRequest
{
    [Required(AllowEmptyStrings = true)] // obligatorio en el contrato OpenAPI; el mensaje lo da la regla propia
    [Rule.OrganizationName]
    public string Name { get; set; } = string.Empty;

    [Rule.PlainText("La descripción", ValidationLimits.CompanyDescriptionMax, MultiLine = true)]
    public string? Description { get; set; }

    [Required(AllowEmptyStrings = true)] // obligatorio en el contrato OpenAPI; el mensaje lo da la regla propia
    [Rule.StrictEmail(Label = "El correo de contacto")]
    public string ContactEmail { get; set; } = string.Empty;

    [Rule.ContactPhone]
    public string? ContactPhone { get; set; }
}
