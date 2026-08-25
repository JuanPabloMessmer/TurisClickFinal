using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Modules.Experiences.Services;

namespace TurisClick.Api.Modules.Experiences.Controllers;

/// <summary>UC-P-10 — Definir disponibilidad de una experiencia.</summary>
[ApiController]
[Route("api/experiences")]
public class ExperienceAvailabilityController(IExperienceAvailabilityService availabilityService) : ControllerBase
{
    [HttpPost("{experienceId:guid}/availability")]
    [Authorize(Policy = "RequireProvider")]
    [ProducesResponseType(typeof(ExperienceAvailabilityResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ExperienceAvailabilityResponse>> Create(
        Guid experienceId, [FromBody] CreateExperienceAvailabilityRequest request, CancellationToken ct)
    {
        var result = await availabilityService.CreateAsync(experienceId, request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Vista de gestión del PROVIDER dueño — todos los slots, cualquier fecha/estado.</summary>
    [HttpGet("mine/{experienceId:guid}/availability")]
    [Authorize(Policy = "RequireProvider")]
    public async Task<ActionResult<List<ExperienceAvailabilityResponse>>> ListOwned(Guid experienceId, CancellationToken ct)
    {
        var result = await availabilityService.ListOwnedAsync(experienceId, ct);
        return Ok(result);
    }

    /// <summary>Vista pública para elegir fecha/slot al reservar (UC-T-08) — solo experiencias PUBLISHED.</summary>
    [HttpGet("{experienceId:guid}/availability")]
    [AllowAnonymous]
    public async Task<ActionResult<List<ExperienceAvailabilityResponse>>> ListPublic(Guid experienceId, CancellationToken ct)
    {
        var result = await availabilityService.ListPublicAsync(experienceId, ct);
        return Ok(result);
    }
}
