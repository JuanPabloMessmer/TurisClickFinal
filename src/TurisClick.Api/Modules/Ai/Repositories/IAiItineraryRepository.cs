using TurisClick.Api.Modules.Ai.Entities;

namespace TurisClick.Api.Modules.Ai.Repositories;

public interface IAiItineraryRepository
{
    /// <summary>AsNoTracking, con Items + Experience/Package/Destination + Availabilities cargados — detalle completo (UC-T-14).</summary>
    Task<AiItinerary?> GetByIdForReadAsync(Guid id, CancellationToken ct);

    /// <summary>El itinerario más reciente de una conversación (UC-T-14: "la propuesta vigente").</summary>
    Task<AiItinerary?> GetLatestByConversationIdAsync(Guid conversationId, CancellationToken ct);

    /// <summary>Trackeado (sin AsNoTracking) — para cambiar Status en UC-T-16.</summary>
    Task<AiItinerary?> GetByIdForUpdateAsync(Guid id, CancellationToken ct);

    /// <summary>UC-T-17 — "Mis itinerarios guardados": solo los SAVED del turista dueño, más recientes primero.</summary>
    Task<(List<AiItinerary> Items, int TotalCount)> ListSavedByTouristAsync(Guid touristId, int page, int pageSize, CancellationToken ct);

    Task AddAsync(AiItinerary itinerary, CancellationToken ct);
}
