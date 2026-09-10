namespace TurisClick.Api.Modules.Admin.Dtos;

/// <summary>
/// UC-A-07 — vista mínima que necesita el ADMIN al sancionar contenido. No reutiliza
/// ExperienceResponse/PackageResponse a propósito: esos DTOs pasan por el guard de propiedad del
/// proveedor dueño, y un admin no es dueño de nada.
/// </summary>
public class AdminContentResponse
{
    public Guid Id { get; set; }

    /// <summary>`EXPERIENCE` o `PACKAGE`.</summary>
    public string ProductType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid CompanyId { get; set; }
}
