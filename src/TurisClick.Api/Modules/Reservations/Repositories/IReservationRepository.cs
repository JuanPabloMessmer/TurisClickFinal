using TurisClick.Api.Modules.Reservations.Entities;

namespace TurisClick.Api.Modules.Reservations.Repositories;

public interface IReservationRepository
{
    /// <summary>UC-T-18 — la reserva ya creada desde un itinerario IA (relación 1-1, ver domain-model.md). Sirve para responder "ya estaba reservado, es esta".</summary>
    Task<Reservation?> GetByAiItineraryIdAsync(Guid aiItineraryId, CancellationToken ct);

    /// <summary>UC-SYS-08 — ids de reservas PENDING_PAYMENT cuyo hold ya venció. Sin lock: la autoridad es la transición condicional posterior.</summary>
    Task<List<Guid>> ListExpiredCandidateIdsAsync(DateTimeOffset now, int batchSize, CancellationToken ct);

    /// <summary>Trackeado y con sus ítems — para cancelar (UC-T-11).</summary>
    Task<Reservation?> GetByIdForCancellationAsync(Guid id, CancellationToken ct);

    Task AddAsync(Reservation reservation, CancellationToken ct);

    /// <summary>AsNoTracking, con Items + Experience + Company + ExperienceAvailability cargados — detalle completo para el TOURIST dueño.</summary>
    Task<Reservation?> GetByIdForReadAsync(Guid id, CancellationToken ct);

    /// <summary>UC-T-19 — tracked (no AsNoTracking), con los mismos Includes que GetByIdForReadAsync, para poder mutar Status/ConfirmedAt/Items y persistir con SaveChangesAsync.</summary>
    Task<Reservation?> GetByIdForPaymentAsync(Guid id, CancellationToken ct);

    /// <summary>UC-T-10 — "Mis reservas" del TOURIST autenticado.</summary>
    Task<(List<Reservation> Items, int TotalCount)> ListByTouristAsync(Guid touristId, int page, int pageSize, CancellationToken ct);
}
