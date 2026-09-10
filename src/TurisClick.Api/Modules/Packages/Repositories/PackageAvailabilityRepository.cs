using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Packages.Entities;

namespace TurisClick.Api.Modules.Packages.Repositories;

public class PackageAvailabilityRepository(TurisClickDbContext db) : IPackageAvailabilityRepository
{
    public Task<PackageAvailability?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.PackageAvailabilities.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<PackageAvailability?> GetByIdWithPackageAsync(Guid id, CancellationToken ct) =>
        db.PackageAvailabilities
            .AsNoTracking()
            .Include(a => a.Package).ThenInclude(p => p!.Company)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<bool> ExistsAsync(Guid packageId, DateOnly departureDate, CancellationToken ct) =>
        db.PackageAvailabilities.AnyAsync(a => a.PackageId == packageId && a.DepartureDate == departureDate, ct);

    public Task<List<PackageAvailability>> ListAllAsync(Guid packageId, CancellationToken ct) =>
        db.PackageAvailabilities
            .AsNoTracking()
            .Where(a => a.PackageId == packageId)
            .OrderBy(a => a.DepartureDate)
            .ToListAsync(ct);

    public Task<List<PackageAvailability>> ListBookableAsync(Guid packageId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return db.PackageAvailabilities
            .AsNoTracking()
            .Where(a => a.PackageId == packageId
                && a.Status == AvailabilitySlotStatus.OPEN
                && a.DepartureDate >= today
                && a.ReservedSlots < a.TotalSlots)
            .OrderBy(a => a.DepartureDate)
            .ToListAsync(ct);
    }

    public async Task AddAsync(PackageAvailability availability, CancellationToken ct) =>
        await db.PackageAvailabilities.AddAsync(availability, ct);
}
