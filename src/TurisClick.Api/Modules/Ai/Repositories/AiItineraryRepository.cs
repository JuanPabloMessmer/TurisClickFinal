using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Ai.Entities;

namespace TurisClick.Api.Modules.Ai.Repositories;

public class AiItineraryRepository(TurisClickDbContext db) : IAiItineraryRepository
{
    public Task<AiItinerary?> GetByIdForReadAsync(Guid id, CancellationToken ct) =>
        db.AiItineraries
            .AsNoTracking()
            .AsSplitQuery()
            .Include(i => i.Items).ThenInclude(it => it.Experience)
            .Include(i => i.Items).ThenInclude(it => it.Package)
            .Include(i => i.Items).ThenInclude(it => it.ExperienceAvailability)
            .Include(i => i.Items).ThenInclude(it => it.PackageAvailability)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

    public Task<AiItinerary?> GetLatestByConversationIdAsync(Guid conversationId, CancellationToken ct) =>
        db.AiItineraries
            .AsNoTracking()
            .AsSplitQuery()
            .Include(i => i.Items).ThenInclude(it => it.Experience)
            .Include(i => i.Items).ThenInclude(it => it.Package)
            .Include(i => i.Items).ThenInclude(it => it.ExperienceAvailability)
            .Include(i => i.Items).ThenInclude(it => it.PackageAvailability)
            .Where(i => i.AiConversationId == conversationId)
            .OrderByDescending(i => i.Version)
            .ThenByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(AiItinerary itinerary, CancellationToken ct) =>
        await db.AiItineraries.AddAsync(itinerary, ct);
}
