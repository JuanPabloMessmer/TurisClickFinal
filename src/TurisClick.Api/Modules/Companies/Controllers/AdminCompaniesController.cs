using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Companies.Dtos;
using TurisClick.Api.Modules.Companies.Entities;
using TurisClick.Api.Modules.Companies.Services;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Companies.Controllers;

/// <summary>UC-A-01/02/03 — Revisar, aprobar y rechazar solicitudes de empresa. Exclusivo de ADMIN.</summary>
[ApiController]
[Route("api/admin/companies")]
[Authorize(Policy = "RequireAdmin")]
public class AdminCompaniesController(ICompanyService companyService, ICurrentUserContext currentUser) : ControllerBase
{
    /// <summary>UC-A-01. status es opcional; sin filtro devuelve todas las empresas.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<CompanyResponse>>> List(
        [FromQuery] CompanyStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await companyService.ListAsync(status, page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CompanyResponse>> GetById(Guid id, CancellationToken ct)
    {
        var result = await companyService.GetByIdAsync(id, ct);
        return Ok(result);
    }

    /// <summary>UC-A-02.</summary>
    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<CompanyResponse>> Approve(Guid id, CancellationToken ct)
    {
        var result = await companyService.ApproveAsync(id, currentUser.UserId, ct);
        return Ok(result);
    }

    /// <summary>UC-A-03.</summary>
    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<CompanyResponse>> Reject(Guid id, [FromBody] RejectCompanyRequest request, CancellationToken ct)
    {
        var result = await companyService.RejectAsync(id, currentUser.UserId, request, ct);
        return Ok(result);
    }
}
