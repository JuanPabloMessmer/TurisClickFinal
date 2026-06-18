using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Users.DTOs;
using TurisClick.Api.Modules.Users.Services;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Admin;

[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = "ADMIN")]
public class AdminUsersController : ControllerBase
{
    private readonly IUserService _userService;

    public AdminUsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserResponse>>> GetAll()
    {
        return Ok(await _userService.GetAllAsync());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserResponse>> GetById(int id)
    {
        try
        {
            return Ok(await _userService.GetByIdAsync(id));
        }
        catch (NotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }

    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request)
    {
        try
        {
            var response = await _userService.CreateAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = response.UserId }, response);
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
    public async Task<ActionResult<UserResponse>> Update(int id, UpdateUserRequest request)
    {
        try
        {
            return Ok(await _userService.UpdateAsync(id, request));
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
    public async Task<ActionResult<UserResponse>> ChangeStatus(int id, ChangeUserStatusRequest request)
    {
        try
        {
            return Ok(await _userService.ChangeStatusAsync(id, request));
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

    [HttpPatch("{id:int}/role")]
    public async Task<ActionResult<UserResponse>> ChangeRole(int id, ChangeUserRoleRequest request)
    {
        try
        {
            return Ok(await _userService.ChangeRoleAsync(id, request));
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
