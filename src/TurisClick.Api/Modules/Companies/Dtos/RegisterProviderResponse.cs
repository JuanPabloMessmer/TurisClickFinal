using TurisClick.Api.Modules.Auth.Dtos;

namespace TurisClick.Api.Modules.Companies.Dtos;

/// <summary>
/// Extiende AuthResultResponse (mismo patrón que UC-AUTH-01: registrar = autenticar) agregando el
/// resumen de la empresa recién creada, que todavía está PENDING_APPROVAL.
/// </summary>
public class RegisterProviderResponse : AuthResultResponse
{
    public CompanySummaryResponse Company { get; set; } = new();
}
