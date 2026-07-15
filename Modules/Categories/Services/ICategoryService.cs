using TurisClick.Api.Modules.Categories.DTOs;

namespace TurisClick.Api.Modules.Categories.Services;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryResponse>> GetAllAsync();
    Task<CategoryResponse> GetByIdAsync(int categoryId);
    Task<CategoryResponse> CreateAsync(CreateCategoryRequest request);
    Task<CategoryResponse> UpdateAsync(int categoryId, UpdateCategoryRequest request);
    Task<CategoryResponse> ChangeStatusAsync(int categoryId, ChangeCategoryStatusRequest request);
}
