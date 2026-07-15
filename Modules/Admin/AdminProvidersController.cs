using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Providers.DTOs;
using TurisClick.Api.Modules.Providers.Services;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Admin;

[ApiController]
[Route("api/backoffice/providers")]
[Authorize(Roles = "ADMIN")]
public class AdminProvidersController : ControllerBase
{
    private readonly IProviderService _providerService;

    public AdminProvidersController(IProviderService providerService)
    {
        _providerService = providerService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProviderResponse>>> GetAll()
    {
        return Ok(await _providerService.GetAllAsync());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProviderResponse>> GetById(int id)
    {
        try
        {
            return Ok(await _providerService.GetByIdAsync(id));
        }
        catch (NotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }

    [HttpPost]
    public async Task<ActionResult<ProviderResponse>> Create(CreateProviderRequest request)
    {
        try
        {
            var response = await _providerService.CreateAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = response.ProviderId }, response);
        }
        catch (ConflictException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (NotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (ValidationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ProviderResponse>> Update(int id, UpdateProviderRequest request)
    {
        try
        {
            return Ok(await _providerService.UpdateAsync(id, request));
        }
        catch (ConflictException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (NotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }

    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult<ProviderResponse>> ChangeStatus(int id, ChangeProviderStatusRequest request)
    {
        try
        {
            return Ok(await _providerService.ChangeStatusAsync(id, request));
        }
        catch (NotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (ValidationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
