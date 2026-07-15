using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Categories.DTOs;
using TurisClick.Api.Modules.Categories.Services;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Categories;

[ApiController]
[Route("api/backoffice/categories")]
[Authorize(Roles = "ADMIN,PROVIDER")]
public class CategoryController : ControllerBase
{
    private readonly ICategoryService _categoryService;

    public CategoryController(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryResponse>>> GetAll()
    {
        return Ok(await _categoryService.GetAllAsync());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CategoryResponse>> GetById(int id)
    {
        try
        {
            return Ok(await _categoryService.GetByIdAsync(id));
        }
        catch (NotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }

    [HttpPost]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<CategoryResponse>> Create(CreateCategoryRequest request)
    {
        try
        {
            var response = await _categoryService.CreateAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = response.CategoryId }, response);
        }
        catch (ConflictException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<CategoryResponse>> Update(int id, UpdateCategoryRequest request)
    {
        try
        {
            return Ok(await _categoryService.UpdateAsync(id, request));
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
    public async Task<ActionResult<CategoryResponse>> ChangeStatus(int id, ChangeCategoryStatusRequest request)
    {
        try
        {
            return Ok(await _categoryService.ChangeStatusAsync(id, request));
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
