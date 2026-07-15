using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Destinations.DTOs;
using TurisClick.Api.Modules.Destinations.Services;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Destinations;

[ApiController]
[Route("api/backoffice/destinations")]
[Authorize(Roles = "ADMIN,PROVIDER")]
public class DestinationController : ControllerBase
{
    private readonly IDestinationService _destinationService;

    public DestinationController(IDestinationService destinationService)
    {
        _destinationService = destinationService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DestinationResponse>>> GetAll()
    {
        return Ok(await _destinationService.GetAllAsync());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<DestinationResponse>> GetById(int id)
    {
        try
        {
            return Ok(await _destinationService.GetByIdAsync(id));
        }
        catch (NotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }

    [HttpPost]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<DestinationResponse>> Create(CreateDestinationRequest request)
    {
        try
        {
            var response = await _destinationService.CreateAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = response.DestinationId }, response);
        }
        catch (ConflictException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<DestinationResponse>> Update(int id, UpdateDestinationRequest request)
    {
        try
        {
            return Ok(await _destinationService.UpdateAsync(id, request));
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
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<DestinationResponse>> ChangeStatus(
        int id,
        ChangeDestinationStatusRequest request)
    {
        try
        {
            return Ok(await _destinationService.ChangeStatusAsync(id, request));
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
