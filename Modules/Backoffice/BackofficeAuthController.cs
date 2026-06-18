using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Auth.DTOs;
using TurisClick.Api.Modules.Auth.Services;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Backoffice;

[ApiController]
[Route("api/backoffice/auth")]
[AllowAnonymous]
public class BackofficeAuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public BackofficeAuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        try
        {
            return Ok(await _authService.LoginBackofficeAsync(request));
        }
        catch (UnauthorizedException exception)
        {
            return Unauthorized(new { message = exception.Message });
        }
        catch (ForbiddenException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message });
        }
        catch (NotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }
}
