using TurisClick.Api.Modules.Categories.Dtos;

namespace TurisClick.Api.Modules.Ai.Dtos;

/// <summary>Vista completa — UC-T-12/13, detalle de una conversación propia.</summary>
public class ConversationResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;

    public PreferencesResponse Preferences { get; set; } = new();

    public List<MessageResponse> Messages { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Fila de "mis conversaciones" — más liviana que ConversationResponse (sin historial de mensajes).</summary>
public class ConversationSummaryResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PreferredDestinationName { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class MessageResponse
{
    public Guid Id { get; set; }
    public string Sender { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Preferencias interpretadas tal como quedaron persistidas en AiConversation (UC-AI-01) — nunca texto
/// libre del LLM, siempre el estado ya fusionado y validado por el backend.
/// </summary>
public class PreferencesResponse
{
    public Guid? PreferredDestinationId { get; set; }
    public string? PreferredDestinationName { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int? DurationDays { get; set; }
    public int? TravelersCount { get; set; }
    public decimal? BudgetTotal { get; set; }
    public string? BudgetCurrency { get; set; }
    public string? RestrictionsNotes { get; set; }

    /// <summary>Ritmo pedido en la conversación (RELAXED/BALANCED/INTENSE) o null.</summary>
    public string? TravelPace { get; set; }

    public List<CategoryResponse> Categories { get; set; } = [];
}
