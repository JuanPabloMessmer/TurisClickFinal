using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Packages.Entities;

namespace TurisClick.Api.Modules.Ai.Repositories;

public class AiCatalogRepository(TurisClickDbContext db) : IAiCatalogRepository
{
    public async Task<List<Experience>> SearchCandidateExperiencesAsync(AiCatalogFilter filter, CancellationToken ct)
    {
        var query = db.Experiences.AsNoTracking().Where(e => e.Status == PublicationStatus.PUBLISHED);

        if (filter.DestinationId.HasValue)
            query = query.Where(e => e.DestinationId == filter.DestinationId);

        if (filter.PriceMax.HasValue)
            query = query.Where(e => e.Price <= filter.PriceMax);

        query = query.Where(e => e.Availabilities.Any(a =>
            a.Status == AvailabilitySlotStatus.OPEN
            && a.ReservedSlots < a.TotalSlots
            && (filter.DateFrom == null || a.Date >= filter.DateFrom)
            && (filter.DateTo == null || a.Date <= filter.DateTo)));

        return await query
            .AsSplitQuery()
            .Include(e => e.Destination)
            .Include(e => e.Categories)
            .Include(e => e.Availabilities.Where(a =>
                a.Status == AvailabilitySlotStatus.OPEN
                && a.ReservedSlots < a.TotalSlots
                && (filter.DateFrom == null || a.Date >= filter.DateFrom)
                && (filter.DateTo == null || a.Date <= filter.DateTo)))
            .OrderByDescending(e => e.CreatedAt)
            .Take(filter.SqlLimit)
            .ToListAsync(ct);
    }

    public async Task<List<Package>> SearchCandidatePackagesAsync(AiCatalogFilter filter, CancellationToken ct)
    {
        var query = db.Packages.AsNoTracking().Where(p => p.Status == PublicationStatus.PUBLISHED);

        if (filter.DestinationId.HasValue)
            query = query.Where(p => p.DestinationId == filter.DestinationId);

        if (filter.PriceMax.HasValue)
            query = query.Where(p => p.Price <= filter.PriceMax);

        query = query.Where(p => p.Availabilities.Any(a =>
            a.Status == AvailabilitySlotStatus.OPEN
            && a.ReservedSlots < a.TotalSlots
            && (filter.DateFrom == null || a.DepartureDate >= filter.DateFrom)
            && (filter.DateTo == null || a.DepartureDate <= filter.DateTo)));

        return await query
            .AsSplitQuery()
            .Include(p => p.Destination)
            .Include(p => p.Categories)
            .Include(p => p.Availabilities.Where(a =>
                a.Status == AvailabilitySlotStatus.OPEN
                && a.ReservedSlots < a.TotalSlots
                && (filter.DateFrom == null || a.DepartureDate >= filter.DateFrom)
                && (filter.DateTo == null || a.DepartureDate <= filter.DateTo)))
            .OrderByDescending(p => p.CreatedAt)
            .Take(filter.SqlLimit)
            .ToListAsync(ct);
    }
}
