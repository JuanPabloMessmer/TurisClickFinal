using TurisClick.Api.Modules.Ai.Dtos;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Ai.Services;

public interface IAiConversationService
{
    /// <summary>UC-T-12. El Tourist sale del JWT, nunca del request.</summary>
    Task<ConversationResponse> CreateAsync(CancellationToken ct);

    /// <summary>"Mis conversaciones" — exclusivo del Tourist dueño.</summary>
    Task<PagedResult<ConversationSummaryResponse>> ListMineAsync(int page, int pageSize, CancellationToken ct);

    /// <summary>403 si la conversación no pertenece al Tourist autenticado.</summary>
    Task<ConversationResponse> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>UC-T-13 — orquesta UC-AI-01 (extracción) y, si hay suficiente información, UC-AI-02/03/04 (retrieval + composición).</summary>
    Task<SendMessageResponse> SendMessageAsync(Guid conversationId, SendMessageRequest request, CancellationToken ct);

    /// <summary>UC-T-14 — la propuesta vigente (más reciente) de la conversación.</summary>
    Task<ItineraryResponse> GetLatestItineraryAsync(Guid conversationId, CancellationToken ct);

    /// <summary>Detalle de un itinerario por id — 403 si no pertenece al Tourist autenticado.</summary>
    Task<ItineraryResponse> GetItineraryByIdAsync(Guid itineraryId, CancellationToken ct);
}
