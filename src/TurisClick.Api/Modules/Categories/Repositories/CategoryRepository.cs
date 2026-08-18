using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Categories.Entities;

namespace TurisClick.Api.Modules.Categories.Repositories;

public class CategoryRepository(TurisClickDbContext db) : ICategoryRepository
{
    public Task<Category?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<List<Category>> ListAsync(CancellationToken ct) =>
        db.Categories.OrderBy(c => c.Name).ToListAsync(ct);

    public Task<bool> ExistsWithNameAsync(string name, Guid? excludeId, CancellationToken ct)
    {
        var query = db.Categories.Where(c => c.Name == name);

        if (excludeId.HasValue)
            query = query.Where(c => c.Id != excludeId.Value);

        return query.AnyAsync(ct);
    }

    public async Task AddAsync(Category category, CancellationToken ct) =>
        await db.Categories.AddAsync(category, ct);

    public void Remove(Category category) => db.Categories.Remove(category);
}
