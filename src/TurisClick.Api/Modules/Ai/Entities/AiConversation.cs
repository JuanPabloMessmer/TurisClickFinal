using TurisClick.Api.Modules.Auth.Entities;
using TurisClick.Api.Modules.Categories.Entities;
using TurisClick.Api.Modules.Destinations.Entities;

namespace TurisClick.Api.Modules.Ai.Entities;

/// <summary>
/// docs/domain-model.md §8. Soporta UC-T-12/13/15. Absorbe TripPreferences como atributos propios
/// (relación 1—1 sin ciclo de vida propio, ver justificación en el documento de dominio) — se
/// actualizan incrementalmente por UC-AI-01 a medida que la conversación avanza, nunca se reemplazan
/// desde cero.
/// </summary>
public class AiConversation
{
    public Guid Id { get; set; }

    public Guid TouristId { get; set; }
    public User? Tourist { get; set; }

    public AiConversationStatus Status { get; set; } = AiConversationStatus.ACTIVE;

    // ---- Preferencias interpretadas (UC-AI-01) ----
    public Guid? PreferredDestinationId { get; set; }
    public Destination? PreferredDestination { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int? TravelersCount { get; set; }
    public decimal? BudgetTotal { get; set; }
    public string? BudgetCurrency { get; set; }

    /// <summary>Opcional — puede derivarse de StartDate/EndDate si ambas existen; se guarda aparte para cuando el turista da duración sin fechas concretas.</summary>
    public int? DurationDays { get; set; }

    public string? RestrictionsNotes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<AiMessage> Messages { get; set; } = new List<AiMessage>();
    public ICollection<AiItinerary> Itineraries { get; set; } = new List<AiItinerary>();

    /// <summary>Intereses del turista — categorías reales del catálogo, no texto libre (UC-AI-02 filtra por categoría real).</summary>
    public ICollection<Category> Categories { get; set; } = new List<Category>();
}
