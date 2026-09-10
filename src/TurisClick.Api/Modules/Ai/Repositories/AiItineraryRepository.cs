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
            .Include(i => i.Items).ThenInclude(it => it.Experience).ThenInclude(e => e!.Categories)
            .Include(i => i.Items).ThenInclude(it => it.Package).ThenInclude(p => p!.Categories)
            .Include(i => i.Items).ThenInclude(it => it.ExperienceAvailability)
            .Include(i => i.Items).ThenInclude(it => it.PackageAvailability)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

    public Task<AiItinerary?> GetLatestByConversationIdAsync(Guid conversationId, CancellationToken ct) =>
        db.AiItineraries
            .AsNoTracking()
            .AsSplitQuery()
            .Include(i => i.Items).ThenInclude(it => it.Experience).ThenInclude(e => e!.Categories)
            .Include(i => i.Items).ThenInclude(it => it.Package).ThenInclude(p => p!.Categories)
            .Include(i => i.Items).ThenInclude(it => it.ExperienceAvailability)
            .Include(i => i.Items).ThenInclude(it => it.PackageAvailability)
            .Where(i => i.AiConversationId == conversationId)
            .OrderByDescending(i => i.Version)
            .ThenByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public Task<AiItinerary?> GetByIdForUpdateAsync(Guid id, CancellationToken ct) =>
        db.AiItineraries.FirstOrDefaultAsync(i => i.Id == id, ct);

    public Task<AiItinerary?> GetByIdForBookingAsync(Guid id, CancellationToken ct) =>
        db.AiItineraries
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<(List<AiItinerary> Items, int TotalCount)> ListSavedByTouristAsync(
        Guid touristId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.AiItineraries
            .AsNoTracking()
            .Where(i => i.TouristId == touristId && i.Status == AiItineraryStatus.SAVED);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Include(i => i.Items)
            .OrderByDescending(i => i.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task AddAsync(AiItinerary itinerary, CancellationToken ct) =>
        await db.AiItineraries.AddAsync(itinerary, ct);
}
