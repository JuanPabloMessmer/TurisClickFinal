using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Reservations.Entities;

namespace TurisClick.Api.Modules.Reservations.Repositories;

public class ReservationRepository(TurisClickDbContext db) : IReservationRepository
{
    public async Task AddAsync(Reservation reservation, CancellationToken ct) =>
        await db.Reservations.AddAsync(reservation, ct);

    public Task<Reservation?> GetByIdForReadAsync(Guid id, CancellationToken ct) =>
        db.Reservations
            .AsNoTracking()
            .AsSplitQuery()
            .Include(r => r.Items).ThenInclude(i => i.Company)
            .Include(r => r.Items).ThenInclude(i => i.Experience)
            .Include(r => r.Items).ThenInclude(i => i.ExperienceAvailability)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<(List<Reservation> Items, int TotalCount)> ListByTouristAsync(
        Guid touristId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Reservations.AsNoTracking().Where(r => r.TouristId == touristId);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Include(r => r.Items).ThenInclude(i => i.Company)
            .Include(r => r.Items).ThenInclude(i => i.Experience)
            .Include(r => r.Items).ThenInclude(i => i.ExperienceAvailability)
            .AsSplitQuery()
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }
}
