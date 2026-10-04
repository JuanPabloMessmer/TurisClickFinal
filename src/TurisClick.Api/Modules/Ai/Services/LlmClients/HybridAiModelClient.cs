using TurisClick.Api.Modules.Ai.Services;

namespace TurisClick.Api.Modules.Ai.Services.LlmClients;

/// <summary>
/// Tercer proveedor (`Ai:Provider = Hybrid`): el LLM interpreta el lenguaje y las reglas mandan en los
/// escalares que se pueden leer literalmente del mensaje.
///
/// Sale de lo que mostró el benchmark, no de una intuición (ver docs/ai-evaluation.md): el LLM pierde
/// puntos sobre todo cuando INFIERE de más —cuenta viajeros que el mensaje no nombra, deduce un ritmo del
/// tipo de actividad— y cuando se le escapa una cantidad explícita; el cliente determinístico pierde
/// puntos sólo cuando no entiende el idioma, y en esos casos no devuelve un dato equivocado: devuelve
/// null. Esa asimetría es lo que hace que combinarlos sume en vez de promediar:
///
/// <list type="bullet">
/// <item>Los escalares con evidencia literal —duración, viajeros, fechas y presupuesto— los gana la
/// regla cuando la encontró, porque su regex exige la unidad ("5 días", "4 personas", "800 bolivianos",
/// una fecha ISO): si matcheó, el dato está escrito en el mensaje.</item>
/// <item>Todo lo que depende de entender el idioma —destino, intereses, ritmo, restricciones, la
/// redacción, los refinamientos y la composición— queda en manos del LLM, que es lo que las reglas no
/// pueden hacer en inglés ni con lenguaje libre.</item>
/// </list>
///
/// No agrega guardrails ni los relaja: envuelve al cliente que ya los tiene (FallbackAiModelClient), así
/// que la validación contra el catálogo, el saneo de inyección y el fallback siguen siendo los mismos.
/// </summary>
public class HybridAiModelClient(IAiModelClient language, DeterministicAiModelClient literal, ILogger<HybridAiModelClient> logger) : IAiModelClient
{
    public async Task<PreferenceExtractionResult> ExtractPreferencesAsync(PreferenceExtractionRequest request, CancellationToken ct)
    {
        var fromLanguage = await language.ExtractPreferencesAsync(request, ct);
        var fromRules = await literal.ExtractPreferencesAsync(request, ct);

        // Las fechas y el presupuesto se toman en bloque: media fecha o un monto sin su moneda serían
        // peores que cualquiera de las dos lecturas completas.
        var (startDate, endDate) = fromRules.StartDate is not null || fromRules.EndDate is not null
            ? (fromRules.StartDate, fromRules.EndDate)
            : (fromLanguage.StartDate, fromLanguage.EndDate);

        var (budgetAmount, budgetCurrency, budgetIsPerPerson) = fromRules.BudgetAmount is not null
            ? (fromRules.BudgetAmount, fromRules.BudgetCurrency, fromRules.BudgetIsPerPerson)
            : (fromLanguage.BudgetAmount, fromLanguage.BudgetCurrency, fromLanguage.BudgetIsPerPerson);

        var merged = fromLanguage with
        {
            DurationDays = fromRules.DurationDays ?? fromLanguage.DurationDays,
            TravelersCount = fromRules.TravelersCount ?? fromLanguage.TravelersCount,
            StartDate = startDate,
            EndDate = endDate,
            BudgetAmount = budgetAmount,
            BudgetCurrency = budgetCurrency,
            BudgetIsPerPerson = budgetIsPerPerson,
        };

        if (merged != fromLanguage)
            logger.LogDebug("Hybrid: las reglas corrigieron al menos un escalar de la extracción del modelo.");

        return merged;
    }

    // El resto es lenguaje puro: pedir una aclaración, armar el itinerario, entender un ajuste o explicar
    // un ítem. Ahí las reglas no tienen nada que aportar y se delega tal cual.
    public Task<string> GenerateClarificationReplyAsync(ClarificationRequest request, CancellationToken ct) =>
        language.GenerateClarificationReplyAsync(request, ct);

    public Task<ItineraryCompositionResult> ComposeItineraryAsync(ItineraryCompositionRequest request, CancellationToken ct) =>
        language.ComposeItineraryAsync(request, ct);

    public Task<ModificationIntentResult> InterpretModificationAsync(ModificationInterpretationRequest request, CancellationToken ct) =>
        language.InterpretModificationAsync(request, ct);

    public Task<string> GenerateItemExplanationAsync(ItemExplanationRequest request, CancellationToken ct) =>
        language.GenerateItemExplanationAsync(request, ct);
}
