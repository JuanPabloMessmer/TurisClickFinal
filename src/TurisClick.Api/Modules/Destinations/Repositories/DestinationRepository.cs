using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Destinations.Entities;

namespace TurisClick.Api.Modules.Destinations.Repositories;

public class DestinationRepository(TurisClickDbContext db) : IDestinationRepository
{
    public Task<Destination?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Destinations.FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<Destination?> GetByIdWithParentAsync(Guid id, CancellationToken ct) =>
        db.Destinations.Include(d => d.Parent).FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<List<Destination>> ListAsync(Guid? parentId, DestinationType? type, CancellationToken ct)
    {
        var query = db.Destinations.Include(d => d.Parent).AsQueryable();

        if (parentId.HasValue)
            query = query.Where(d => d.ParentId == parentId);

        if (type.HasValue)
            query = query.Where(d => d.Type == type);

        return query.OrderBy(d => d.Name).ToListAsync(ct);
    }

    public Task<bool> ExistsWithNameAsync(string name, Guid? parentId, DestinationType type, Guid? excludeId, CancellationToken ct)
    {
        var query = db.Destinations.Where(d =>
            d.Name == name && d.ParentId == parentId && d.Type == type);

        if (excludeId.HasValue)
            query = query.Where(d => d.Id != excludeId.Value);

        return query.AnyAsync(ct);
    }

    public Task<bool> HasChildrenAsync(Guid id, CancellationToken ct) =>
        db.Destinations.AnyAsync(d => d.ParentId == id, ct);

    public async Task AddAsync(Destination destination, CancellationToken ct) =>
        await db.Destinations.AddAsync(destination, ct);

    public void Remove(Destination destination) => db.Destinations.Remove(destination);
}
