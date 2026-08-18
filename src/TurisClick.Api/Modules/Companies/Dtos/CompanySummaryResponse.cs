namespace TurisClick.Api.Modules.Companies.Dtos;

/// <summary>Resumen embebido en RegisterProviderResponse — el detalle completo se consulta luego vía GET /api/companies/me.</summary>
public class CompanySummaryResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
