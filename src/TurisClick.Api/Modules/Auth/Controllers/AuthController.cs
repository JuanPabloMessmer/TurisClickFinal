using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Auth.Dtos;
using TurisClick.Api.Modules.Auth.Services;

namespace TurisClick.Api.Modules.Auth.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService, ICurrentUserContext currentUser) : ControllerBase
{
    /// <summary>UC-AUTH-01 — Registrar cuenta de Turista.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResultResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResultResponse>> Register([FromBody] RegisterTouristRequest request, CancellationToken ct)
    {
        var result = await authService.RegisterTouristAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>UC-AUTH-02 — Iniciar sesión.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResultResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResultResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await authService.LoginAsync(request, ct);
        return Ok(result);
    }

    /// <summary>UC-AUTH-03 — Refrescar token de sesión.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResultResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResultResponse>> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        var result = await authService.RefreshAsync(request, ct);
        return Ok(result);
    }

    /// <summary>UC-AUTH-04 — Cerrar sesión (revoca el refresh token indicado).</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken ct)
    {
        await authService.LogoutAsync(currentUser.UserId, request, ct);
        return NoContent();
    }
}
