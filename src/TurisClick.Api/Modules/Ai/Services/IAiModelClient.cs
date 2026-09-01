namespace TurisClick.Api.Modules.Ai.Services;

/// <summary>
/// Abstracción sobre el proveedor de LLM (Ollama local por defecto — ver OllamaAiModelClient/
/// DeterministicAiModelClient). Ningún Service del dominio conoce Ollama/OpenAI/Anthropic directamente;
/// todos dependen solo de esta interfaz (docs de la sesión, sección 5). El modelo NUNCA recibe acceso a
/// PostgreSQL — todo lo que ve viene ya armado y validado por el backend (candidatos, catálogo de
/// nombres conocidos), y todo lo que devuelve se re-valida contra la base antes de persistirse
/// (AiConversationService) — el LLM nunca es fuente de verdad de precio/disponibilidad/IDs.
/// </summary>
public interface IAiModelClient
{
    /// <summary>UC-AI-01 — interpreta el mensaje del turista y devuelve señales estructuradas (nunca texto libre para decisiones).</summary>
    Task<PreferenceExtractionResult> ExtractPreferencesAsync(PreferenceExtractionRequest request, CancellationToken ct);

    /// <summary>Redacta una pregunta natural pidiendo SOLO los campos que el backend ya determinó que faltan (nunca decide qué falta).</summary>
    Task<string> GenerateClarificationReplyAsync(ClarificationRequest request, CancellationToken ct);

    /// <summary>UC-AI-03/04 — dado un set de candidatos reales (con IDs), decide qué candidato va en qué día. Nunca inventa IDs fuera de los candidatos recibidos.</summary>
    Task<ItineraryCompositionResult> ComposeItineraryAsync(ItineraryCompositionRequest request, CancellationToken ct);
}

/// <summary>Un turno de la conversación, para darle contexto al modelo sin que la conversación completa sea la única fuente de verdad.</summary>
public record ConversationTurn(string Sender, string Content);

/// <summary>Lo que ya sabemos de esta conversación antes de este mensaje — el modelo actualiza incrementalmente, nunca reemplaza desde cero (docs/use-cases.md UC-AI-01).</summary>
public record ExtractedPreferencesSnapshot(
    string? PreferredDestinationName,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int? DurationDays,
    int? TravelersCount,
    decimal? BudgetTotal,
    string? BudgetCurrency,
    IReadOnlyList<string> CategoryNames,
    string? RestrictionsNotes);

public record PreferenceExtractionRequest(
    IReadOnlyList<ConversationTurn> History,
    string LatestMessage,
    ExtractedPreferencesSnapshot CurrentPreferences,
    /// <summary>Vocabulario real de destinos (ciudades) y categorías, ya cargado por el backend desde Postgres — el modelo elige entre estos nombres, no inventa nuevos (reduce hallucination de catálogo).</summary>
    IReadOnlyList<string> KnownDestinationNames,
    IReadOnlyList<string> KnownCategoryNames,
    DateOnly Today);

/// <summary>
/// Señales crudas extraídas del mensaje. NO incluye "needsClarification": esa decisión la toma el
/// backend de forma determinística comparando el estado ya fusionado contra los campos obligatorios
/// para poder buscar (destino, fechas-o-duración, viajeros) — ver AiConversationService.
/// </summary>
public record PreferenceExtractionResult(
    string? DestinationMention,
    IReadOnlyList<string> CategoryMentions,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int? DurationDays,
    int? TravelersCount,
    /// <summary>Monto tal como se mencionó. BudgetIsPerPerson indica si hay que multiplicarlo por TravelersCount para obtener el total (la entidad solo persiste un BudgetTotal agregado, sin columna de "scope" — ver reporte de la sesión).</summary>
    decimal? BudgetAmount,
    string? BudgetCurrency,
    bool BudgetIsPerPerson,
    string? RestrictionsNotes);

public record ClarificationRequest(
    IReadOnlyList<ConversationTurn> History,
    string LatestMessage,
    IReadOnlyList<string> MissingFields);

public record CandidateAvailability(Guid Id, DateOnly Date, int AvailableSlots);

public record CandidateExperience(
    Guid Id,
    string Title,
    string DestinationName,
    decimal Price,
    string Currency,
    IReadOnlyList<string> CategoryNames,
    int? DurationMinutes,
    IReadOnlyList<CandidateAvailability> Availabilities);

public record CandidatePackage(
    Guid Id,
    string Title,
    string DestinationName,
    decimal Price,
    string Currency,
    int DurationDays,
    IReadOnlyList<string> CategoryNames,
    IReadOnlyList<CandidateAvailability> Availabilities,
    /// <summary>UC-AI-03 — calculado de forma determinística por RetrievalService (no por el LLM) antes de ofrecer los candidatos.</summary>
    bool IsStrongFit);

public record ItineraryCompositionRequest(
    ExtractedPreferencesSnapshot Preferences,
    int TripDurationDays,
    IReadOnlyList<CandidateExperience> CandidateExperiences,
    IReadOnlyList<CandidatePackage> CandidatePackages);

public record ComposedItem(int DayNumber, string ProductType, Guid ProductId, Guid? AvailabilityId);

public record ItineraryCompositionResult(string? Title, IReadOnlyList<ComposedItem> Items, string AssistantExplanation);
