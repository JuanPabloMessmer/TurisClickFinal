using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Packages.Dtos;
using TurisClick.Api.Modules.Packages.Repositories;
using TurisClick.Api.Modules.Packages.Services;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Packages.Controllers;

[ApiController]
[Route("api/packages")]
public class PackagesController(IPackageService packageService) : ControllerBase
{
    /// <summary>UC-P-07.</summary>
    [HttpPost]
    [Authorize(Policy = "RequireProvider")]
    [ProducesResponseType(typeof(PackageResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<PackageResponse>> Create([FromBody] CreatePackageRequest request, CancellationToken ct)
    {
        var result = await packageService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetMineById), new { id = result.Id }, result);
    }

    /// <summary>UC-P-08.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "RequireProvider")]
    public async Task<ActionResult<PackageResponse>> Update(Guid id, [FromBody] UpdatePackageRequest request, CancellationToken ct)
    {
        var result = await packageService.UpdateAsync(id, request, ct);
        return Ok(result);
    }

    /// <summary>UC-P-09.</summary>
    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = "RequireProvider")]
    public async Task<ActionResult<PackageResponse>> Publish(Guid id, CancellationToken ct)
    {
        var result = await packageService.PublishAsync(id, ct);
        return Ok(result);
    }

    /// <summary>UC-P-09.</summary>
    [HttpPost("{id:guid}/unpublish")]
    [Authorize(Policy = "RequireProvider")]
    public async Task<ActionResult<PackageResponse>> Unpublish(Guid id, CancellationToken ct)
    {
        var result = await packageService.UnpublishAsync(id, ct);
        return Ok(result);
    }

    /// <summary>"Mis paquetes" — soporte necesario para que el Provider pueda listar y obtener IDs para editar/publicar.</summary>
    [HttpGet("mine")]
    [Authorize(Policy = "RequireProvider")]
    public async Task<ActionResult<PagedResult<PackageSummaryResponse>>> ListMine(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await packageService.ListOwnedAsync(page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("mine/{id:guid}")]
    [Authorize(Policy = "RequireProvider")]
    public async Task<ActionResult<PackageResponse>> GetMineById(Guid id, CancellationToken ct)
    {
        var result = await packageService.GetOwnedByIdAsync(id, ct);
        return Ok(result);
    }

    /// <summary>UC-T-06 — público, solo paquetes PUBLISHED.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<PagedResult<PackageSummaryResponse>>> Search(
        [FromQuery] Guid? destinationId,
        [FromQuery] Guid? categoryId,
        [FromQuery] decimal? priceMin,
        [FromQuery] decimal? priceMax,
        [FromQuery] int? durationDaysMin,
        [FromQuery] int? durationDaysMax,
        [FromQuery] DateOnly? departureFrom,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var filter = new PackageSearchFilter(
            destinationId, categoryId, priceMin, priceMax, durationDaysMin, durationDaysMax, departureFrom, page, pageSize);
        var result = await packageService.SearchAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>UC-T-07 — público, solo si está PUBLISHED (404 en cualquier otro caso).</summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<PackageResponse>> GetPublishedById(Guid id, CancellationToken ct)
    {
        var result = await packageService.GetPublishedByIdAsync(id, ct);
        return Ok(result);
    }
}
