using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Categories.Dtos;
using TurisClick.Api.Modules.Categories.Entities;
using TurisClick.Api.Modules.Categories.Repositories;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Categories.Services;

public class CategoryService(ICategoryRepository categoryRepository, TurisClickDbContext db) : ICategoryService
{
    public async Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();

        if (await categoryRepository.ExistsWithNameAsync(name, excludeId: null, ct))
            throw new ConflictAppException("Ya existe una categoría con ese nombre.");

        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = request.Description?.Trim()
        };

        await categoryRepository.AddAsync(category, ct);
        await db.SaveChangesAsync(ct);

        return ToResponse(category);
    }

    public async Task<CategoryResponse> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken ct)
    {
        var category = await categoryRepository.GetByIdAsync(id, ct)
            ?? throw new NotFoundAppException("Categoría no encontrada.");

        var name = request.Name.Trim();

        if (await categoryRepository.ExistsWithNameAsync(name, excludeId: id, ct))
            throw new ConflictAppException("Ya existe una categoría con ese nombre.");

        category.Name = name;
        category.Description = request.Description?.Trim();
        await db.SaveChangesAsync(ct);

        return ToResponse(category);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var category = await categoryRepository.GetByIdAsync(id, ct)
            ?? throw new NotFoundAppException("Categoría no encontrada.");

        categoryRepository.Remove(category);
        await db.SaveChangesAsync(ct);
    }

    public async Task<CategoryResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var category = await categoryRepository.GetByIdAsync(id, ct)
            ?? throw new NotFoundAppException("Categoría no encontrada.");

        return ToResponse(category);
    }

    public async Task<List<CategoryResponse>> ListAsync(CancellationToken ct)
    {
        var categories = await categoryRepository.ListAsync(ct);
        return categories.Select(ToResponse).ToList();
    }

    private static CategoryResponse ToResponse(Category category) => new()
    {
        Id = category.Id,
        Name = category.Name,
        Description = category.Description
    };
}
