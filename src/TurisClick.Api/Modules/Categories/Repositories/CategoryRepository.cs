using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Categories.Entities;
using TurisClick.Api.Shared.Validation;

namespace TurisClick.Api.Modules.Categories.Repositories;

public class CategoryRepository(TurisClickDbContext db) : ICategoryRepository
{
    // SQL fijo armado con una constante del código (NameKeySql); la clave y el id van como parámetros.
    private static readonly string NameConflictSql =
        $"SELECT name AS \"Value\" FROM categories WHERE {NameKeySql.NameKey("name")} = {{0}} AND id <> {{1}} LIMIT 1";

    public Task<Category?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<List<Category>> ListAsync(CancellationToken ct) =>
        db.Categories.OrderBy(c => c.Name).ToListAsync(ct);

    public Task<List<Category>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        ids.Count == 0
            ? Task.FromResult(new List<Category>())
            : db.Categories.Where(c => ids.Contains(c.Id)).ToListAsync(ct);

    public Task<string?> FindNameConflictAsync(string nameKey, Guid? excludeId, CancellationToken ct) =>
        db.Database
            .SqlQueryRaw<string>(NameConflictSql, nameKey, excludeId ?? Guid.Empty)
            .FirstOrDefaultAsync(ct);

    public async Task<CategoryUsage> GetUsageAsync(Guid id, CancellationToken ct)
    {
        var row = await db.Database
            .SqlQueryRaw<UsageRow>(
                """
                SELECT (SELECT count(*) FROM experience_categories WHERE category_id = {0})::int AS "Experiences",
                       (SELECT count(*) FROM package_categories WHERE category_id = {0})::int AS "Packages",
                       (SELECT count(*) FROM tourist_preference_categories WHERE category_id = {0})::int AS "Tourists"
                """, id)
            .SingleAsync(ct);
        return new CategoryUsage(row.Experiences, row.Packages, row.Tourists);
    }

    private sealed class UsageRow
    {
        public int Experiences { get; set; }
        public int Packages { get; set; }
        public int Tourists { get; set; }
    }

    public async Task AddAsync(Category category, CancellationToken ct) =>
        await db.Categories.AddAsync(category, ct);

    public void Remove(Category category) => db.Categories.Remove(category);
}
