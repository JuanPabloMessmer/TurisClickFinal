using TurisClick.Api.Modules.Categories.Dtos;

namespace TurisClick.Api.Modules.Categories.Services;

public interface ICategoryService
{
    Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, CancellationToken ct);
    Task<CategoryResponse> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<CategoryResponse> GetByIdAsync(Guid id, CancellationToken ct);
    Task<List<CategoryResponse>> ListAsync(CancellationToken ct);
}
