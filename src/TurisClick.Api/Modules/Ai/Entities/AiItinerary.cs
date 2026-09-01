using TurisClick.Api.Modules.Auth.Entities;

namespace TurisClick.Api.Modules.Ai.Entities;

/// <summary>
/// docs/domain-model.md §8. Soporta UC-T-14 en Oleada 5 (UC-T-16/17/18 quedan para oleadas futuras —
/// ver AiItineraryStatus). Es la propuesta persistible (decisión 7): no es un producto de catálogo, no
/// pertenece a ningún Provider, y puede combinar productos de varias Companies.
/// </summary>
public class AiItinerary
{
    public Guid Id { get; set; }

    public Guid AiConversationId { get; set; }
    public AiConversation? AiConversation { get; set; }

    /// <summary>Denormalizado a propósito (mismo criterio que AiConversation.TouristId) para listar itinerarios propios sin pasar por la conversación.</summary>
    public Guid TouristId { get; set; }
    public User? Tourist { get; set; }

    public string? Title { get; set; }

    public AiItineraryStatus Status { get; set; } = AiItineraryStatus.DRAFT;

    /// <summary>Se incrementa en cada ajuste (UC-AI-05, Oleada 6) — en Oleada 5 siempre queda en 1.</summary>
    public int Version { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<AiItineraryItem> Items { get; set; } = new List<AiItineraryItem>();
}
