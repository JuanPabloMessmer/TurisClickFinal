using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Admin.Dtos;
using TurisClick.Api.Modules.Admin.Services;

namespace TurisClick.Api.Modules.Admin.Controllers;

/// <summary>
/// UC-A-07 — retirar contenido de circulación por incumplir políticas. Un producto SUSPENDED deja de
/// estar PUBLISHED, así que desaparece del catálogo público y del retrieval de la IA automáticamente
/// (todas esas consultas filtran por PUBLISHED). Solo el ADMIN puede levantar la sanción.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = "RequireAdmin")]
public class AdminContentController(IAdminContentService contentService) : ControllerBase
{
    [HttpPost("experiences/{id:guid}/suspend")]
    public async Task<ActionResult<AdminContentResponse>> SuspendExperience(Guid id, CancellationToken ct) =>
        Ok(await contentService.SuspendExperienceAsync(id, ct));

    [HttpPost("experiences/{id:guid}/restore")]
    public async Task<ActionResult<AdminContentResponse>> RestoreExperience(Guid id, CancellationToken ct) =>
        Ok(await contentService.RestoreExperienceAsync(id, ct));

    [HttpPost("packages/{id:guid}/suspend")]
    public async Task<ActionResult<AdminContentResponse>> SuspendPackage(Guid id, CancellationToken ct) =>
        Ok(await contentService.SuspendPackageAsync(id, ct));

    [HttpPost("packages/{id:guid}/restore")]
    public async Task<ActionResult<AdminContentResponse>> RestorePackage(Guid id, CancellationToken ct) =>
        Ok(await contentService.RestorePackageAsync(id, ct));
}
