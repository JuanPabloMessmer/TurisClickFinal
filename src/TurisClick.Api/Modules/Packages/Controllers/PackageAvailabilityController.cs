using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Packages.Dtos;
using TurisClick.Api.Modules.Packages.Services;

namespace TurisClick.Api.Modules.Packages.Controllers;

/// <summary>UC-P-11 — Definir disponibilidad de un paquete.</summary>
[ApiController]
[Route("api/packages")]
public class PackageAvailabilityController(IPackageAvailabilityService availabilityService) : ControllerBase
{
    [HttpPost("{packageId:guid}/availability")]
    [Authorize(Policy = "RequireProvider")]
    [ProducesResponseType(typeof(PackageAvailabilityResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<PackageAvailabilityResponse>> Create(
        Guid packageId, [FromBody] CreatePackageAvailabilityRequest request, CancellationToken ct)
    {
        var result = await availabilityService.CreateAsync(packageId, request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Vista de gestión del PROVIDER dueño — todas las salidas, cualquier fecha/estado.</summary>
    [HttpGet("mine/{packageId:guid}/availability")]
    [Authorize(Policy = "RequireProvider")]
    public async Task<ActionResult<List<PackageAvailabilityResponse>>> ListOwned(Guid packageId, CancellationToken ct)
    {
        var result = await availabilityService.ListOwnedAsync(packageId, ct);
        return Ok(result);
    }

    /// <summary>Vista pública para elegir fecha de salida al reservar (UC-T-09) — solo paquetes PUBLISHED.</summary>
    [HttpGet("{packageId:guid}/availability")]
    [AllowAnonymous]
    public async Task<ActionResult<List<PackageAvailabilityResponse>>> ListPublic(Guid packageId, CancellationToken ct)
    {
        var result = await availabilityService.ListPublicAsync(packageId, ct);
        return Ok(result);
    }
}
