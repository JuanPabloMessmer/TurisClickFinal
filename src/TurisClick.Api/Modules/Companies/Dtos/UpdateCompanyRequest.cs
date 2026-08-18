using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Companies.Dtos;

/// <summary>
/// UC-P-02 — Gestionar perfil de "Mi Empresa". Solo datos públicos/de contacto: LegalDocument no es
/// editable por el propio proveedor (identificador legal fijado al registrarse) y Status lo controla
/// únicamente el ADMIN (UC-A-02/03).
/// </summary>
public class UpdateCompanyRequest
{
    [Required, MinLength(2), MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Required, EmailAddress, MaxLength(255)]
    public string ContactEmail { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? ContactPhone { get; set; }
}
