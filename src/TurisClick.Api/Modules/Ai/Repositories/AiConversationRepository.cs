using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Ai.Entities;

namespace TurisClick.Api.Modules.Ai.Repositories;

public class AiConversationRepository(TurisClickDbContext db) : IAiConversationRepository
{
    public Task<AiConversation?> GetByIdForReadAsync(Guid id, CancellationToken ct) =>
        db.AiConversations
            .AsNoTracking()
            .AsSplitQuery()
            .Include(c => c.Messages.OrderBy(m => m.CreatedAt))
            .Include(c => c.Categories)
            .Include(c => c.PreferredDestination)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<AiConversation?> GetByIdForUpdateAsync(Guid id, CancellationToken ct) =>
        db.AiConversations
            .AsSplitQuery()
            .Include(c => c.Categories)
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<(List<AiConversation> Items, int TotalCount)> ListByTouristAsync(
        Guid touristId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.AiConversations.AsNoTracking().Where(c => c.TouristId == touristId);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Include(c => c.PreferredDestination)
            .OrderByDescending(c => c.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task AddAsync(AiConversation conversation, CancellationToken ct) =>
        await db.AiConversations.AddAsync(conversation, ct);
}
