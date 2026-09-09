using TurisClick.Api.Modules.Ai.Dtos;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Ai.Services;

/// <summary>
/// UC-T-16/17 y UC-AI-06 — ciclo de vida del itinerario ya propuesto (guardar, listar guardados,
/// retomar, explicar un componente). La iteración (UC-T-15) NO vive acá: pasa por el mismo endpoint de
/// mensajes de la conversación (docs/use-cases.md UC-T-15), así que la orquesta AiConversationService.
/// </summary>
public interface IAiItineraryService
{
    /// <summary>Detalle por id, revalidado contra el catálogo vigente. 403 si no pertenece al Tourist autenticado.</summary>
    Task<ItineraryResponse> GetByIdAsync(Guid itineraryId, CancellationToken ct);

    /// <summary>UC-T-16 — marca la propuesta como SAVED. Idempotente; nunca reserva ni retiene cupo.</summary>
    Task<ItineraryResponse> SaveAsync(Guid itineraryId, CancellationToken ct);

    /// <summary>UC-T-17 — "Mis itinerarios guardados".</summary>
    Task<PagedResult<SavedItinerarySummaryResponse>> ListSavedMineAsync(int page, int pageSize, CancellationToken ct);

    /// <summary>UC-AI-06 — por qué este componente está en el itinerario, a partir de hechos reales.</summary>
    Task<ItemExplanationResponse> GetItemExplanationAsync(Guid itineraryId, Guid itemId, CancellationToken ct);
}
