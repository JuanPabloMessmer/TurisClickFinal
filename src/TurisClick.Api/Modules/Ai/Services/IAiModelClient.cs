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

    /// <summary>UC-AI-03/04/05 — dado un set de candidatos reales (con IDs), decide qué candidato va en qué día. Nunca inventa IDs fuera de los candidatos recibidos. En una iteración (UC-AI-05) recibe además los ítems preservados y la instrucción del turista.</summary>
    Task<ItineraryCompositionResult> ComposeItineraryAsync(ItineraryCompositionRequest request, CancellationToken ct);

    /// <summary>
    /// UC-AI-05 — interpreta si el mensaje es un ajuste sobre la propuesta vigente y qué ítems toca.
    /// Solo puede referirse a ítems por los IDs que el backend le pasó; cualquier otro se descarta.
    /// Nunca decide el reemplazo acá: eso pasa por retrieval + composición + revalidación.
    /// </summary>
    Task<ModificationIntentResult> InterpretModificationAsync(ModificationInterpretationRequest request, CancellationToken ct);

    /// <summary>
    /// UC-AI-06 — redacta la justificación de un componente. El modelo NO recibe la base ni infiere
    /// nada: recibe hechos ya calculados por el backend desde Postgres y solo los pone en prosa
    /// (docs/use-cases.md UC-AI-06: "sin agregar precios o datos no presentes en ellos").
    /// </summary>
    Task<string> GenerateItemExplanationAsync(ItemExplanationRequest request, CancellationToken ct);
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

/// <summary>
/// Ítem del itinerario vigente que el backend decidió CONSERVAR en una iteración (UC-AI-05, sección 2
/// de la sesión: "si el usuario dice cambiame el día 2, no regeneres los otros 4"). El modelo lo recibe
/// como contexto de solo lectura: no debe volver a proponerlo ni reemplazarlo, el backend lo re-inserta
/// igual (previa revalidación contra Postgres).
/// </summary>
public record PreservedItem(int DayNumber, string ProductType, Guid ProductId, string Title);

public record ItineraryCompositionRequest(
    ExtractedPreferencesSnapshot Preferences,
    int TripDurationDays,
    IReadOnlyList<CandidateExperience> CandidateExperiences,
    IReadOnlyList<CandidatePackage> CandidatePackages,
    /// <summary>Vacío en la primera generación (UC-AI-04); poblado al iterar (UC-AI-05).</summary>
    IReadOnlyList<PreservedItem> PreservedItems,
    /// <summary>Null en la primera generación; el pedido textual del turista al iterar ("quitá el rafting").</summary>
    string? ModificationInstruction);

public record ComposedItem(int DayNumber, string ProductType, Guid ProductId, Guid? AvailabilityId);

public record ItineraryCompositionResult(string? Title, IReadOnlyList<ComposedItem> Items, string AssistantExplanation);

/// <summary>Vista de un ítem del itinerario vigente. El Id es la única forma de referirse a él: el backend descarta cualquier id que el modelo no haya recibido acá.</summary>
public record CurrentItineraryItemView(
    Guid ItemId,
    int DayNumber,
    string ProductType,
    string Title,
    IReadOnlyList<string> CategoryNames,
    decimal EstimatedUnitPrice,
    string Currency,
    DateOnly? Date);

/// <summary>Qué tipo de ajuste pidió el turista (UC-AI-05). El backend traduce esto a "qué preservo / qué reemplazo" de forma determinística.</summary>
public enum ModificationAction
{
    /// <summary>No es un ajuste sobre la propuesta vigente — se trata como búsqueda nueva (UC-AI-02/03/04).</summary>
    NONE,
    REMOVE,
    REPLACE,
    ADD,
    REDUCE_BUDGET,
    PREFER_PACKAGE
}

public record ModificationInterpretationRequest(
    IReadOnlyList<ConversationTurn> History,
    string LatestMessage,
    ExtractedPreferencesSnapshot Preferences,
    IReadOnlyList<CurrentItineraryItemView> CurrentItems,
    IReadOnlyList<string> KnownCategoryNames);

public record ModificationIntentResult(
    ModificationAction Action,
    /// <summary>Ítems actuales afectados. El backend valida que cada id esté en CurrentItems antes de usarlo.</summary>
    IReadOnlyList<Guid> TargetItemIds,
    IReadOnlyList<int> TargetDays,
    /// <summary>Categorías reales que el turista quiere sumar ("agregá algo de aventura") — se resuelven contra el catálogo, no se inventan.</summary>
    IReadOnlyList<string> AddCategoryNames);

/// <summary>
/// UC-AI-06. Facts ya viene calculado por el backend desde Postgres (precio real, cupos reales, encaje
/// con fechas/presupuesto/intereses). El modelo solo redacta: no puede agregar atributos que no estén acá.
/// </summary>
public record ItemExplanationRequest(
    ExtractedPreferencesSnapshot Preferences,
    CurrentItineraryItemView Item,
    IReadOnlyList<string> Facts);
