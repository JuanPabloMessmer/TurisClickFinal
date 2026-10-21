using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Flights.Entities;
using TurisClick.Api.Modules.Reservations.Entities;

namespace TurisClick.Api.Modules.Reservations.Repositories;

public class ReservationRepository(TurisClickDbContext db) : IReservationRepository
{
    public Task<Reservation?> GetByAiItineraryIdAsync(Guid aiItineraryId, CancellationToken ct) =>
        db.Reservations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.AiItineraryId == aiItineraryId, ct);

    /// <summary>
    /// Reservas vencidas que el lote puede expirar de verdad. Las que tienen el vuelo en emisión (ORDERING) o
    /// esperando reconciliación no se pueden expirar hasta que eso se resuelva, así que ni siquiera entran al
    /// lote: si entraran, bastaría con tener más de `batchSize` trabadas —las más viejas, que el orden pone
    /// primero— para que el lote se llenara de reservas que se saltean y las demás no vencieran nunca.
    /// ExpireAsync conserva su propio control, que es el que cubre la carrera entre este SELECT y el UPDATE.
    /// </summary>
    public Task<List<Guid>> ListExpiredCandidateIdsAsync(DateTimeOffset now, int batchSize, CancellationToken ct) =>
        db.Reservations
            .AsNoTracking()
            .Where(r => r.Status == ReservationStatus.PENDING_PAYMENT && r.ExpiresAt != null && r.ExpiresAt < now)
            .Where(r => !db.FlightBookings.Any(b => b.ReservationId == r.Id
                && (b.Status == FlightBookingStatus.ORDERING || b.Status == FlightBookingStatus.RECONCILIATION_REQUIRED)))
            .OrderBy(r => r.ExpiresAt)
            .Take(batchSize)
            .Select(r => r.Id)
            .ToListAsync(ct);

    public Task<Reservation?> GetByIdForCancellationAsync(Guid id, CancellationToken ct) =>
        db.Reservations
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task AddAsync(Reservation reservation, CancellationToken ct) =>
        await db.Reservations.AddAsync(reservation, ct);

    public Task<Reservation?> GetByIdForReadAsync(Guid id, CancellationToken ct) =>
        db.Reservations
            .AsNoTracking()
            .AsSplitQuery()
            .Include(r => r.Items).ThenInclude(i => i.Company)
            .Include(r => r.Items).ThenInclude(i => i.Experience)
            .Include(r => r.Items).ThenInclude(i => i.ExperienceAvailability)
            .Include(r => r.Items).ThenInclude(i => i.Package)
            .Include(r => r.Items).ThenInclude(i => i.PackageAvailability)
            .Include(r => r.Items).ThenInclude(i => i.ProviderFlightDeparture).ThenInclude(d => d!.ProviderFlight)
            .Include(r => r.Items).ThenInclude(i => i.Passengers)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<Reservation?> GetByIdForPaymentAsync(Guid id, CancellationToken ct) =>
        db.Reservations
            .AsSplitQuery()
            .Include(r => r.Items).ThenInclude(i => i.Company)
            .Include(r => r.Items).ThenInclude(i => i.Experience)
            .Include(r => r.Items).ThenInclude(i => i.ExperienceAvailability)
            .Include(r => r.Items).ThenInclude(i => i.Package)
            .Include(r => r.Items).ThenInclude(i => i.PackageAvailability)
            .Include(r => r.Items).ThenInclude(i => i.ProviderFlightDeparture).ThenInclude(d => d!.ProviderFlight)
            .Include(r => r.Items).ThenInclude(i => i.Passengers)
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
            .Include(r => r.Items).ThenInclude(i => i.Package)
            .Include(r => r.Items).ThenInclude(i => i.PackageAvailability)
            .Include(r => r.Items).ThenInclude(i => i.ProviderFlightDeparture).ThenInclude(d => d!.ProviderFlight)
            .Include(r => r.Items).ThenInclude(i => i.Passengers)
            .AsSplitQuery()
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }
}
