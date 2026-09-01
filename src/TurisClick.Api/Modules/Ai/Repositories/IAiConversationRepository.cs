using TurisClick.Api.Modules.Ai.Entities;

namespace TurisClick.Api.Modules.Ai.Repositories;

public interface IAiConversationRepository
{
    /// <summary>AsNoTracking, con Messages/Categories/PreferredDestination — para devolver el detalle completo (UC-T-12/13).</summary>
    Task<AiConversation?> GetByIdForReadAsync(Guid id, CancellationToken ct);

    /// <summary>Tracked, con Categories cargadas — para poder fusionar preferencias (UC-AI-01) y persistir mensajes/itinerarios en la misma transacción.</summary>
    Task<AiConversation?> GetByIdForUpdateAsync(Guid id, CancellationToken ct);

    Task<(List<AiConversation> Items, int TotalCount)> ListByTouristAsync(Guid touristId, int page, int pageSize, CancellationToken ct);

    Task AddAsync(AiConversation conversation, CancellationToken ct);
}
