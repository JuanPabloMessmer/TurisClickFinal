using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Admin.Dtos;
using TurisClick.Api.Modules.Admin.Services;
using TurisClick.Api.Modules.Auth.Entities;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Admin.Controllers;

/// <summary>UC-A-06 — gestión de cuentas. Suspender bloquea login y refresh (ya lo aplica AuthService).</summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = "RequireAdmin")]
public class AdminUsersController(IAdminUserService userService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AdminUserResponse>>> List(
        [FromQuery] UserRole? role, [FromQuery] UserStatus? status, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await userService.ListAsync(role, status, search, page, pageSize, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/suspend")]
    public async Task<ActionResult<AdminUserResponse>> Suspend(Guid id, CancellationToken ct)
    {
        var result = await userService.SuspendAsync(id, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult<AdminUserResponse>> Activate(Guid id, CancellationToken ct)
    {
        var result = await userService.ActivateAsync(id, ct);
        return Ok(result);
    }
}
