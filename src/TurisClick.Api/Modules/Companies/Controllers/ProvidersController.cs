using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Companies.Dtos;
using TurisClick.Api.Modules.Companies.Services;

namespace TurisClick.Api.Modules.Companies.Controllers;

[ApiController]
[Route("api/providers")]
public class ProvidersController(ICompanyService companyService) : ControllerBase
{
    /// <summary>UC-P-01 — Registrar empresa y solicitar cuenta de Provider.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(RegisterProviderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterProviderResponse>> Register([FromBody] RegisterProviderRequest request, CancellationToken ct)
    {
        var result = await companyService.RegisterProviderAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}
