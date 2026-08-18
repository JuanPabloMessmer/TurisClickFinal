using TurisClick.Api.Modules.Categories.Entities;

namespace TurisClick.Api.Modules.Categories.Repositories;

public interface ICategoryRepository
{
    Task<Category?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<List<Category>> ListAsync(CancellationToken ct);
    Task<bool> ExistsWithNameAsync(string name, Guid? excludeId, CancellationToken ct);
    Task AddAsync(Category category, CancellationToken ct);
    void Remove(Category category);
}
