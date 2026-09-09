using TurisClick.Api.Modules.Ai.Services;

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

    /// <summary>
    /// UC-T-17 — resultado de revalidar el itinerario contra el catálogo vigente (precio cambiado, sin
    /// cupos, despublicado). Guardar un itinerario NO congela precio ni retiene cupo, así que esta lista
    /// es la forma de dejarlo explícito al retomarlo.
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>false si al menos un componente ya no se podría reservar tal como está (despublicado, sin fecha o sin cupos).</summary>
    public bool IsStillBookable { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Fila de "Mis itinerarios guardados" (UC-T-17) — más liviana que ItineraryResponse (sin ítems ni revalidación).</summary>
public class SavedItinerarySummaryResponse
{
    public Guid Id { get; set; }
    public Guid AiConversationId { get; set; }
    public string? Title { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Version { get; set; }
    public int ItemCount { get; set; }
    public List<ItineraryTotalResponse> Totals { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>UC-AI-06 — explicación de un componente concreto.</summary>
public class ItemExplanationResponse
{
    public Guid ItineraryId { get; set; }
    public Guid ItemId { get; set; }

    /// <summary>Texto redactado por el modelo a partir de Facts — nunca de datos propios del modelo.</summary>
    public string Explanation { get; set; } = string.Empty;

    /// <summary>
    /// Los hechos que el backend calculó desde Postgres y le pasó al modelo. Se devuelven para que la
    /// explicación sea auditable: todo lo que dice el texto tiene que salir de acá.
    /// </summary>
    public List<string> Facts { get; set; } = [];
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

    // ---- Estado vigente (UC-T-17): nunca pisa el snapshot de arriba, se muestra al lado ----

    /// <summary>Precio actual del producto en el catálogo. Puede diferir de EstimatedUnitPrice: el snapshot es del momento de la propuesta y no se reescribe nunca.</summary>
    public decimal? CurrentPrice { get; set; }
    public string? CurrentCurrency { get; set; }
    public int? CurrentAvailableSlots { get; set; }

    /// <summary>true si el precio vigente difiere del snapshot EN LA MISMA MONEDA (nunca se compara entre monedas distintas).</summary>
    public bool PriceChanged { get; set; }

    /// <summary>`ItemAvailabilityState`: AVAILABLE, PRODUCT_NOT_FOUND, UNPUBLISHED, SLOT_CLOSED o SOLD_OUT.</summary>
    public string AvailabilityState { get; set; } = nameof(ItemAvailabilityState.AVAILABLE);

    /// <summary>false si este componente ya no se podría reservar tal como está.</summary>
    public bool IsStillAvailable { get; set; } = true;

    /// <summary>Avisos específicos de este componente (cambio de precio, sin cupos, despublicado).</summary>
    public List<string> Warnings { get; set; } = [];
}

public class ItineraryTotalResponse
{
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
