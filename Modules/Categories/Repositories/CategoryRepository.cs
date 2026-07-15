using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;

namespace TurisClick.Api.Modules.Categories.Repositories;

public class CategoryRepository : ICategoryRepository
{
    private readonly AppDbContext _dbContext;

    public CategoryRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Category>> GetAllAsync()
    {
        return await _dbContext.Categories
            .AsNoTracking()
            .OrderBy(category => category.Name)
            .ToListAsync();
    }

    public async Task<Category?> GetByIdAsync(int categoryId)
    {
        return await _dbContext.Categories
            .FirstOrDefaultAsync(category => category.CategoryId == categoryId);
    }

    public async Task<bool> NameExistsAsync(string name, int? excludedCategoryId = null)
    {
        return await _dbContext.Categories.AnyAsync(category =>
            category.Name == name &&
            (!excludedCategoryId.HasValue || category.CategoryId != excludedCategoryId.Value));
    }

    public async Task<Category> CreateAsync(Category category)
    {
        _dbContext.Categories.Add(category);
        await _dbContext.SaveChangesAsync();

        return category;
    }

    public async Task SaveChangesAsync()
    {
        await _dbContext.SaveChangesAsync();
    }
}
