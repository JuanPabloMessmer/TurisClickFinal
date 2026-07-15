namespace TurisClick.Api.Modules.Categories.Repositories;

public interface ICategoryRepository
{
    Task<IReadOnlyList<Category>> GetAllAsync();
    Task<Category?> GetByIdAsync(int categoryId);
    Task<bool> NameExistsAsync(string name, int? excludedCategoryId = null);
    Task<Category> CreateAsync(Category category);
    Task SaveChangesAsync();
}
