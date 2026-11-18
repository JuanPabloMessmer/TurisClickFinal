namespace TurisClick.Api.Modules.Companies.Dtos;

/// <summary>
/// Resultado de <c>GET /api/admin/companies/name-check</c> (sólo ADMIN, sólo lectura).
///
/// <see cref="ExactMatch"/> es un duplicado que la API va a rechazar (misma clave sin mayúsculas, tildes ni
/// espacios repetidos). <see cref="Similar"/> son empresas con un nombre parecido —sólo difieren en
/// puntuación o forma societaria, como "Andes Tours S.R.L." y "Andes Tours SRL"—: es una advertencia para el
/// administrador, no un bloqueo (docs/auditoria-validaciones.md, C-3).
/// </summary>
public class CompanyNameCheckResponse
{
    /// <summary>El nombre tal como se guardaría (normalizado).</summary>
    public string Name { get; set; } = string.Empty;

    public CompanyNameMatchResponse? ExactMatch { get; set; }
    public List<CompanyNameMatchResponse> Similar { get; set; } = [];
}

public class CompanyNameMatchResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
