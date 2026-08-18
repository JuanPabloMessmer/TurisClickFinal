using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Destinations.Entities;
using TurisClick.Api.Modules.Destinations.Services;

namespace TurisClick.Api.Modules.Destinations.Controllers;

/// <summary>UC-A-04 — Gestionar destinos. Catálogo maestro, exclusivo de ADMIN.</summary>
[ApiController]
[Route("api/admin/destinations")]
[Authorize(Policy = "RequireAdmin")]
public class DestinationsController(IDestinationService destinationService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<DestinationResponse>>> List(
        [FromQuery] Guid? parentId, [FromQuery] DestinationType? type, CancellationToken ct)
    {
        var result = await destinationService.ListAsync(parentId, type, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DestinationResponse>> GetById(Guid id, CancellationToken ct)
    {
        var result = await destinationService.GetByIdAsync(id, ct);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(DestinationResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<DestinationResponse>> Create([FromBody] CreateDestinationRequest request, CancellationToken ct)
    {
        var result = await destinationService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DestinationResponse>> Update(Guid id, [FromBody] UpdateDestinationRequest request, CancellationToken ct)
    {
        var result = await destinationService.UpdateAsync(id, request, ct);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await destinationService.DeleteAsync(id, ct);
        return NoContent();
    }
}
