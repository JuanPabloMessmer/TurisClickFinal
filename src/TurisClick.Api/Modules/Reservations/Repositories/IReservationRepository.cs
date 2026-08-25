using TurisClick.Api.Modules.Reservations.Entities;

namespace TurisClick.Api.Modules.Reservations.Repositories;

public interface IReservationRepository
{
    Task AddAsync(Reservation reservation, CancellationToken ct);

    /// <summary>AsNoTracking, con Items + Experience + Company + ExperienceAvailability cargados — detalle completo para el TOURIST dueño.</summary>
    Task<Reservation?> GetByIdForReadAsync(Guid id, CancellationToken ct);

    /// <summary>UC-T-10 — "Mis reservas" del TOURIST autenticado.</summary>
    Task<(List<Reservation> Items, int TotalCount)> ListByTouristAsync(Guid touristId, int page, int pageSize, CancellationToken ct);
}
