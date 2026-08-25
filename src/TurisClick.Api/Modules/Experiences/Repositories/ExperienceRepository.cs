using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Experiences.Entities;

namespace TurisClick.Api.Modules.Experiences.Repositories;

public class ExperienceRepository(TurisClickDbContext db) : IExperienceRepository
{
    public Task<Experience?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Experiences.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<Experience?> GetByIdForReadAsync(Guid id, CancellationToken ct) =>
        db.Experiences
            .AsNoTracking()
            .AsSplitQuery()
            .Include(e => e.Destination)
            .Include(e => e.Company)
            .Include(e => e.Categories)
            .Include(e => e.Images)
            .FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<Experience?> GetByIdForUpdateAsync(Guid id, CancellationToken ct) =>
        db.Experiences
            .AsSplitQuery()
            .Include(e => e.Categories)
            .Include(e => e.Images)
            .FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<(List<Experience> Items, int TotalCount)> SearchAsync(ExperienceSearchFilter filter, CancellationToken ct)
    {
        var query = db.Experiences
            .AsNoTracking()
            .Where(e => e.Status == PublicationStatus.PUBLISHED);

        if (filter.DestinationId.HasValue)
            query = query.Where(e => e.DestinationId == filter.DestinationId);

        if (filter.CategoryId.HasValue)
            query = query.Where(e => e.Categories.Any(c => c.Id == filter.CategoryId));

        if (filter.PriceMin.HasValue)
            query = query.Where(e => e.Price >= filter.PriceMin);

        if (filter.PriceMax.HasValue)
            query = query.Where(e => e.Price <= filter.PriceMax);

        if (filter.AvailableFrom.HasValue)
            query = query.Where(e => e.Availabilities.Any(a =>
                a.Status == AvailabilitySlotStatus.OPEN
                && a.Date >= filter.AvailableFrom
                && a.ReservedSlots < a.TotalSlots));

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Include(e => e.Destination)
            .Include(e => e.Company)
            .Include(e => e.Images.Where(i => i.IsCover))
            .OrderByDescending(e => e.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<(List<Experience> Items, int TotalCount)> ListByCompanyAsync(
        Guid companyId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Experiences.AsNoTracking().Where(e => e.CompanyId == companyId);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Include(e => e.Destination)
            .Include(e => e.Company)
            .Include(e => e.Images.Where(i => i.IsCover))
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public Task<bool> HasFutureOpenAvailabilityAsync(Guid experienceId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return db.ExperienceAvailabilities.AnyAsync(a =>
            a.ExperienceId == experienceId
            && a.Status == AvailabilitySlotStatus.OPEN
            && a.Date >= today
            && a.ReservedSlots < a.TotalSlots, ct);
    }

    public async Task AddAsync(Experience experience, CancellationToken ct) =>
        await db.Experiences.AddAsync(experience, ct);
}
