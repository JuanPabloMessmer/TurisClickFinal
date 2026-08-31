using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Reservations.Entities;

namespace TurisClick.Api.Modules.Reservations.Repositories;

public class ReservationItemRepository(TurisClickDbContext db) : IReservationItemRepository
{
    public async Task<(List<ReservationItem> Items, int TotalCount)> ListByCompanyAsync(
        Guid companyId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.ReservationItems.AsNoTracking().Where(i => i.CompanyId == companyId);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Include(i => i.Reservation).ThenInclude(r => r!.Tourist)
            .Include(i => i.Experience)
            .Include(i => i.ExperienceAvailability)
            .Include(i => i.Package)
            .Include(i => i.PackageAvailability)
            .AsSplitQuery()
            .OrderByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public Task<ReservationItem?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.ReservationItems
            .AsNoTracking()
            .Include(i => i.Reservation).ThenInclude(r => r!.Tourist)
            .Include(i => i.Experience)
            .Include(i => i.ExperienceAvailability)
            .Include(i => i.Package)
            .Include(i => i.PackageAvailability)
            .Include(i => i.Company)
            .AsSplitQuery()
            .FirstOrDefaultAsync(i => i.Id == id, ct);
}
