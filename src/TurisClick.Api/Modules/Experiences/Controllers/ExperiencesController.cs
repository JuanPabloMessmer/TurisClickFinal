using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Modules.Experiences.Repositories;
using TurisClick.Api.Modules.Experiences.Services;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Experiences.Controllers;

[ApiController]
[Route("api/experiences")]
public class ExperiencesController(IExperienceService experienceService) : ControllerBase
{
    /// <summary>UC-P-04.</summary>
    [HttpPost]
    [Authorize(Policy = "RequireProvider")]
    [ProducesResponseType(typeof(ExperienceResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ExperienceResponse>> Create([FromBody] CreateExperienceRequest request, CancellationToken ct)
    {
        var result = await experienceService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetMineById), new { id = result.Id }, result);
    }

    /// <summary>UC-P-05.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "RequireProvider")]
    public async Task<ActionResult<ExperienceResponse>> Update(Guid id, [FromBody] UpdateExperienceRequest request, CancellationToken ct)
    {
        var result = await experienceService.UpdateAsync(id, request, ct);
        return Ok(result);
    }

    /// <summary>UC-P-06.</summary>
    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = "RequireProvider")]
    public async Task<ActionResult<ExperienceResponse>> Publish(Guid id, CancellationToken ct)
    {
        var result = await experienceService.PublishAsync(id, ct);
        return Ok(result);
    }

    /// <summary>UC-P-06.</summary>
    [HttpPost("{id:guid}/unpublish")]
    [Authorize(Policy = "RequireProvider")]
    public async Task<ActionResult<ExperienceResponse>> Unpublish(Guid id, CancellationToken ct)
    {
        var result = await experienceService.UnpublishAsync(id, ct);
        return Ok(result);
    }

    /// <summary>"Mis experiencias" — soporte necesario para que el Provider pueda listar y obtener IDs para editar/publicar.</summary>
    [HttpGet("mine")]
    [Authorize(Policy = "RequireProvider")]
    public async Task<ActionResult<PagedResult<ExperienceSummaryResponse>>> ListMine(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await experienceService.ListOwnedAsync(page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("mine/{id:guid}")]
    [Authorize(Policy = "RequireProvider")]
    public async Task<ActionResult<ExperienceResponse>> GetMineById(Guid id, CancellationToken ct)
    {
        var result = await experienceService.GetOwnedByIdAsync(id, ct);
        return Ok(result);
    }

    /// <summary>UC-T-04 — público, solo experiencias PUBLISHED.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<PagedResult<ExperienceSummaryResponse>>> Search(
        [FromQuery] Guid? destinationId,
        [FromQuery] Guid? categoryId,
        [FromQuery] decimal? priceMin,
        [FromQuery] decimal? priceMax,
        [FromQuery] DateOnly? availableFrom,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var filter = new ExperienceSearchFilter(destinationId, categoryId, priceMin, priceMax, availableFrom, page, pageSize);
        var result = await experienceService.SearchAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>UC-T-05 — público, solo si está PUBLISHED (404 en cualquier otro caso).</summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<ExperienceResponse>> GetPublishedById(Guid id, CancellationToken ct)
    {
        var result = await experienceService.GetPublishedByIdAsync(id, ct);
        return Ok(result);
    }
}
