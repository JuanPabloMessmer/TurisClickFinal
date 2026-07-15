using Microsoft.EntityFrameworkCore;
using Npgsql;
using TurisClick.Api.Modules.Categories.DTOs;
using TurisClick.Api.Modules.Categories.Repositories;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Categories.Services;

public class CategoryService : ICategoryService
{
    private static readonly HashSet<string> AllowedStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "ACTIVE", "INACTIVE" };

    private readonly ICategoryRepository _categoryRepository;

    public CategoryService(ICategoryRepository categoryRepository)
    {
        _categoryRepository = categoryRepository;
    }

    public async Task<IReadOnlyList<CategoryResponse>> GetAllAsync()
    {
        var categories = await _categoryRepository.GetAllAsync();
        return categories.Select(MapToResponse).ToList();
    }

    public async Task<CategoryResponse> GetByIdAsync(int categoryId)
    {
        var category = await GetRequiredCategoryAsync(categoryId);
        return MapToResponse(category);
    }

    public async Task<CategoryResponse> CreateAsync(CreateCategoryRequest request)
    {
        var name = NormalizeName(request.Name);

        if (await _categoryRepository.NameExistsAsync(name))
        {
            throw new ConflictException("Category name is already registered.");
        }

        var category = new Category
        {
            Name = name,
            Description = NormalizeOptional(request.Description),
            Status = "ACTIVE",
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await _categoryRepository.CreateAsync(category);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new ConflictException("Category name is already registered.");
        }

        return MapToResponse(category);
    }

    public async Task<CategoryResponse> UpdateAsync(int categoryId, UpdateCategoryRequest request)
    {
        var category = await GetRequiredCategoryAsync(categoryId);
        var name = NormalizeName(request.Name);

        if (await _categoryRepository.NameExistsAsync(name, category.CategoryId))
        {
            throw new ConflictException("Category name is already registered.");
        }

        category.Name = name;
        category.Description = NormalizeOptional(request.Description);

        try
        {
            await _categoryRepository.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new ConflictException("Category name is already registered.");
        }

        return MapToResponse(category);
    }

    public async Task<CategoryResponse> ChangeStatusAsync(int categoryId, ChangeCategoryStatusRequest request)
    {
        var category = await GetRequiredCategoryAsync(categoryId);
        var status = request.Status.Trim().ToUpperInvariant();

        if (!AllowedStatuses.Contains(status))
        {
            throw new ValidationException("Status must be ACTIVE or INACTIVE.");
        }

        category.Status = status;
        await _categoryRepository.SaveChangesAsync();

        return MapToResponse(category);
    }

    private async Task<Category> GetRequiredCategoryAsync(int categoryId)
    {
        return await _categoryRepository.GetByIdAsync(categoryId)
            ?? throw new NotFoundException("Category was not found.");
    }

    private static CategoryResponse MapToResponse(Category category)
    {
        return new CategoryResponse
        {
            CategoryId = category.CategoryId,
            Name = category.Name,
            Description = category.Description,
            Status = category.Status,
            CreatedAt = category.CreatedAt
        };
    }

    private static string NormalizeName(string name)
    {
        return name.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        };
    }
}
