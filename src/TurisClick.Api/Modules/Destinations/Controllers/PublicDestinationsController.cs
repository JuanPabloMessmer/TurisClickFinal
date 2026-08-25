using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Destinations.Entities;
using TurisClick.Api.Modules.Destinations.Services;

namespace TurisClick.Api.Modules.Destinations.Controllers;

/// <summary>UC-T-03 — Explorar destinos. Público, distinto de /api/admin/destinations (gestión, UC-A-04).</summary>
[ApiController]
[Route("api/destinations")]
[AllowAnonymous]
public class PublicDestinationsController(IPublicDestinationService publicDestinationService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PublicDestinationResponse>>> List(
        [FromQuery] Guid? parentId, [FromQuery] DestinationType? type, CancellationToken ct)
    {
        var result = await publicDestinationService.ListAsync(parentId, type, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PublicDestinationResponse>> GetById(Guid id, CancellationToken ct)
    {
        var result = await publicDestinationService.GetByIdAsync(id, ct);
        return Ok(result);
    }
}
