using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Packages.Entities;
using TurisClick.Api.Modules.Companies.Entities;

namespace TurisClick.Api.Modules.Ai.Repositories;

public class AiCatalogRepository(TurisClickDbContext db) : IAiCatalogRepository
{
    /// <summary>
    /// Una disponibilidad de ayer ya no es reservable: el booking la rechaza (UC-T-18). Si el retrieval la
    /// ofreciera, el asistente propondría un itinerario que después no se puede reservar, así que el piso
    /// de fechas es siempre hoy, exista o no un rango pedido por el turista.
    /// </summary>
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task<List<Experience>> SearchCandidateExperiencesAsync(AiCatalogFilter filter, CancellationToken ct)
    {
        var today = Today;
        // UC-A-08: suspender una empresa oculta su catálogo también para la IA — sin esto el retrieval
        // seguiría proponiendo productos de una empresa sancionada (UC-SYS-09 no necesita reindexar
        // nada porque la consulta ES el índice).
        var query = db.Experiences.AsNoTracking()
            .Where(e => e.Status == PublicationStatus.PUBLISHED
                && e.Company!.Status != CompanyStatus.SUSPENDED);

        if (filter.DestinationId.HasValue)
            query = query.Where(e => e.DestinationId == filter.DestinationId);

        if (filter.PriceMax.HasValue)
            query = query.Where(e => e.Price <= filter.PriceMax);

        query = query.Where(e => e.Availabilities.Any(a =>
            a.Status == AvailabilitySlotStatus.OPEN
            && a.ReservedSlots < a.TotalSlots
            && a.Date >= today
            && (filter.DateFrom == null || a.Date >= filter.DateFrom)
            && (filter.DateTo == null || a.Date <= filter.DateTo)));

        return await query
            .AsSplitQuery()
            .Include(e => e.Destination)
            .Include(e => e.Categories)
            .Include(e => e.Availabilities.Where(a =>
                a.Status == AvailabilitySlotStatus.OPEN
                && a.ReservedSlots < a.TotalSlots
                && a.Date >= today
                && (filter.DateFrom == null || a.Date >= filter.DateFrom)
                && (filter.DateTo == null || a.Date <= filter.DateTo)))
            .OrderByDescending(e => e.CreatedAt)
            .Take(filter.SqlLimit)
            .ToListAsync(ct);
    }

    public async Task<List<Package>> SearchCandidatePackagesAsync(AiCatalogFilter filter, CancellationToken ct)
    {
        var today = Today;
        var query = db.Packages.AsNoTracking()
            .Where(p => p.Status == PublicationStatus.PUBLISHED
                && p.Company!.Status != CompanyStatus.SUSPENDED);

        if (filter.DestinationId.HasValue)
            query = query.Where(p => p.DestinationId == filter.DestinationId);

        if (filter.PriceMax.HasValue)
            query = query.Where(p => p.Price <= filter.PriceMax);

        query = query.Where(p => p.Availabilities.Any(a =>
            a.Status == AvailabilitySlotStatus.OPEN
            && a.ReservedSlots < a.TotalSlots
            && a.DepartureDate >= today
            && (filter.DateFrom == null || a.DepartureDate >= filter.DateFrom)
            && (filter.DateTo == null || a.DepartureDate <= filter.DateTo)));

        return await query
            .AsSplitQuery()
            .Include(p => p.Destination)
            .Include(p => p.Categories)
            .Include(p => p.Availabilities.Where(a =>
                a.Status == AvailabilitySlotStatus.OPEN
                && a.ReservedSlots < a.TotalSlots
                && a.DepartureDate >= today
                && (filter.DateFrom == null || a.DepartureDate >= filter.DateFrom)
                && (filter.DateTo == null || a.DepartureDate <= filter.DateTo)))
            .OrderByDescending(p => p.CreatedAt)
            .Take(filter.SqlLimit)
            .ToListAsync(ct);
    }

    public async Task<List<Experience>> GetExperiencesByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];

        return await db.Experiences
            .AsNoTracking()
            .AsSplitQuery()
            .Include(e => e.Destination)
            .Include(e => e.Categories)
            .Include(e => e.Availabilities)
            .Where(e => ids.Contains(e.Id))
            .ToListAsync(ct);
    }

    public async Task<List<Package>> GetPackagesByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];

        return await db.Packages
            .AsNoTracking()
            .AsSplitQuery()
            .Include(p => p.Destination)
            .Include(p => p.Categories)
            .Include(p => p.Availabilities)
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(ct);
    }
}
