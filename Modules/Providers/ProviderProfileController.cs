using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Providers.DTOs;
using TurisClick.Api.Modules.Providers.Services;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Providers;

[ApiController]
[Route("api/backoffice/provider/profile")]
[Authorize(Roles = "PROVIDER")]
public class ProviderProfileController : ControllerBase
{
    private readonly IProviderService _providerService;

    public ProviderProfileController(IProviderService providerService)
    {
        _providerService = providerService;
    }

    [HttpGet]
    public async Task<ActionResult<ProviderResponse>> GetProfile()
    {
        try
        {
            return Ok(await _providerService.GetProfileAsync(GetCurrentUserId()));
        }
        catch (NotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (UnauthorizedException exception)
        {
            return Unauthorized(new { message = exception.Message });
        }
    }

    [HttpPut]
    public async Task<ActionResult<ProviderResponse>> UpdateProfile(UpdateProviderRequest request)
    {
        try
        {
            return Ok(await _providerService.UpdateProfileAsync(GetCurrentUserId(), request));
        }
        catch (ConflictException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (NotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (UnauthorizedException exception)
        {
            return Unauthorized(new { message = exception.Message });
        }
    }

    private int GetCurrentUserId()
    {
        var userIdValue = User.FindFirstValue("user_id");

        if (!int.TryParse(userIdValue, out var userId))
        {
            throw new UnauthorizedException("User id claim is missing.");
        }

        return userId;
    }
}
