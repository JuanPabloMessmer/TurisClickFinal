namespace TurisClick.Api.Modules.Ai.Dtos;

/// <summary>UC-T-14 — Ver propuesta de itinerario.</summary>
public class ItineraryResponse
{
    public Guid Id { get; set; }
    public Guid AiConversationId { get; set; }
    public string? Title { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Version { get; set; }

    public List<ItineraryItemResponse> Items { get; set; } = [];

    /// <summary>Calculado (nunca persistido): suma de Items agrupada por moneda — mismo criterio que ReservationResponse.Totals (domain-model.md §8, nota sobre totales multi-moneda).</summary>
    public List<ItineraryTotalResponse> Totals { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class ItineraryItemResponse
{
    public Guid Id { get; set; }
    public int DayNumber { get; set; }
    public int SortOrder { get; set; }
    public string ProductType { get; set; } = string.Empty;

    public Guid? ExperienceId { get; set; }
    public string? ExperienceTitle { get; set; }
    public Guid? PackageId { get; set; }
    public string? PackageTitle { get; set; }

    public DateOnly? Date { get; set; }

    public decimal EstimatedUnitPrice { get; set; }
    public string Currency { get; set; } = string.Empty;

    /// <summary>Calculado: EstimatedUnitPrice × AiConversation.TravelersCount (ver reporte de la sesión — AiItineraryItem no persiste Travelers/Subtotal propios).</summary>
    public int Travelers { get; set; }
    public decimal Subtotal { get; set; }
}

public class ItineraryTotalResponse
{
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
