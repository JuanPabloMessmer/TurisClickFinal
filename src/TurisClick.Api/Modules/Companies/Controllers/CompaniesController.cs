using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Companies.Dtos;
using TurisClick.Api.Modules.Companies.Services;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Companies.Controllers;

/// <summary>UC-P-02 — Gestionar perfil de "Mi Empresa". Alcance: únicamente la empresa del PROVIDER autenticado.</summary>
[ApiController]
[Route("api/companies")]
[Authorize(Policy = "RequireProvider")]
public class CompaniesController(ICompanyService companyService, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<CompanyResponse>> GetMyCompany(CancellationToken ct)
    {
        var companyId = RequireCompanyId();
        var result = await companyService.GetMyCompanyAsync(companyId, ct);
        return Ok(result);
    }

    [HttpPut("me")]
    public async Task<ActionResult<CompanyResponse>> UpdateMyCompany([FromBody] UpdateCompanyRequest request, CancellationToken ct)
    {
        var companyId = RequireCompanyId();
        var result = await companyService.UpdateMyCompanyAsync(companyId, request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Defensa en profundidad: el CHECK ck_users_provider_has_company garantiza que todo PROVIDER
    /// tiene company_id, pero el Controller no debería asumirlo silenciosamente si algún día cambia.
    /// </summary>
    private Guid RequireCompanyId() =>
        currentUser.CompanyId ?? throw new ForbiddenAppException("El usuario autenticado no tiene una empresa asociada.");
}
