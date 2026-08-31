using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Packages.Entities;

namespace TurisClick.Api.Modules.Packages.Repositories;

public class PackageRepository(TurisClickDbContext db) : IPackageRepository
{
    public Task<Package?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Package?> GetByIdForReadAsync(Guid id, CancellationToken ct) =>
        db.Packages
            .AsNoTracking()
            .AsSplitQuery()
            .Include(p => p.Destination)
            .Include(p => p.Company)
            .Include(p => p.Categories)
            .Include(p => p.Images)
            .Include(p => p.Items).ThenInclude(i => i.Experience)
            .Include(p => p.Availabilities)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Package?> GetByIdForUpdateAsync(Guid id, CancellationToken ct) =>
        db.Packages
            .AsSplitQuery()
            .Include(p => p.Categories)
            .Include(p => p.Images)
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<(List<Package> Items, int TotalCount)> SearchAsync(PackageSearchFilter filter, CancellationToken ct)
    {
        var query = db.Packages
            .AsNoTracking()
            .Where(p => p.Status == PublicationStatus.PUBLISHED);

        if (filter.DestinationId.HasValue)
            query = query.Where(p => p.DestinationId == filter.DestinationId);

        if (filter.CategoryId.HasValue)
            query = query.Where(p => p.Categories.Any(c => c.Id == filter.CategoryId));

        if (filter.PriceMin.HasValue)
            query = query.Where(p => p.Price >= filter.PriceMin);

        if (filter.PriceMax.HasValue)
            query = query.Where(p => p.Price <= filter.PriceMax);

        if (filter.DurationDaysMin.HasValue)
            query = query.Where(p => p.DurationDays >= filter.DurationDaysMin);

        if (filter.DurationDaysMax.HasValue)
            query = query.Where(p => p.DurationDays <= filter.DurationDaysMax);

        if (filter.DepartureFrom.HasValue)
            query = query.Where(p => p.Availabilities.Any(a =>
                a.Status == AvailabilitySlotStatus.OPEN
                && a.DepartureDate >= filter.DepartureFrom
                && a.ReservedSlots < a.TotalSlots));

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Include(p => p.Destination)
            .Include(p => p.Company)
            .Include(p => p.Images.Where(i => i.IsCover))
            .OrderByDescending(p => p.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<(List<Package> Items, int TotalCount)> ListByCompanyAsync(
        Guid companyId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Packages.AsNoTracking().Where(p => p.CompanyId == companyId);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Include(p => p.Destination)
            .Include(p => p.Company)
            .Include(p => p.Images.Where(i => i.IsCover))
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public Task<bool> HasFutureOpenAvailabilityAsync(Guid packageId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return db.PackageAvailabilities.AnyAsync(a =>
            a.PackageId == packageId
            && a.Status == AvailabilitySlotStatus.OPEN
            && a.DepartureDate >= today
            && a.ReservedSlots < a.TotalSlots, ct);
    }

    /// <summary>UC-A-04 DELETE — usado para dar un 409 de dominio claro en vez de dejar que la FK física falle en Postgres.</summary>
    public Task<bool> ExistsForDestinationAsync(Guid destinationId, CancellationToken ct) =>
        db.Packages.AnyAsync(p => p.DestinationId == destinationId, ct);

    public async Task AddAsync(Package package, CancellationToken ct) =>
        await db.Packages.AddAsync(package, ct);
}
