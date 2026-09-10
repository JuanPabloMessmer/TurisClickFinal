using TurisClick.Api.Modules.Reservations.Entities;

namespace TurisClick.Api.Modules.Reservations.Repositories;

public interface IReservationRepository
{
    /// <summary>UC-T-18 — la reserva ya creada desde un itinerario IA (relación 1-1, ver domain-model.md). Sirve para responder "ya estaba reservado, es esta".</summary>
    Task<Reservation?> GetByAiItineraryIdAsync(Guid aiItineraryId, CancellationToken ct);

    Task AddAsync(Reservation reservation, CancellationToken ct);

    /// <summary>AsNoTracking, con Items + Experience + Company + ExperienceAvailability cargados — detalle completo para el TOURIST dueño.</summary>
    Task<Reservation?> GetByIdForReadAsync(Guid id, CancellationToken ct);

    /// <summary>UC-T-19 — tracked (no AsNoTracking), con los mismos Includes que GetByIdForReadAsync, para poder mutar Status/ConfirmedAt/Items y persistir con SaveChangesAsync.</summary>
    Task<Reservation?> GetByIdForPaymentAsync(Guid id, CancellationToken ct);

    /// <summary>UC-T-10 — "Mis reservas" del TOURIST autenticado.</summary>
    Task<(List<Reservation> Items, int TotalCount)> ListByTouristAsync(Guid touristId, int page, int pageSize, CancellationToken ct);
}
