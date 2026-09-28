using Microsoft.Extensions.Logging;

namespace TurisClick.Api.Modules.Ai.Services.LlmClients;

/// <summary>
/// Decorador que pone al LLM adelante y al cliente determinístico atrás. Cada operación se intenta con el
/// modelo real; si el modelo no está disponible, tarda de más, devuelve JSON inválido o devuelve algo que
/// no pasa los guardrails, se resuelve con el determinístico en vez de romper la conversación.
///
/// Es la pieza que hace que "TurisClick con LLM" nunca sea peor que "TurisClick sin LLM": el LLM agrega
/// comprensión de lenguaje natural, pero jamás es un punto único de falla (Fase 9 del pedido).
/// También limpia la salida del modelo antes de devolverla al dominio: destinos y categorías que no estén
/// en el vocabulario real se descartan acá, no más adelante.
/// </summary>
public class FallbackAiModelClient(
    IAiModelClient primary,
    DeterministicAiModelClient fallback,
    ILogger<FallbackAiModelClient> logger) : IAiModelClient
{
    /// <summary>Qué proveedor resolvió la última operación — sólo para logs y para el benchmark.</summary>
    public string LastUsedProvider { get; private set; } = "primary";

    public async Task<PreferenceExtractionResult> ExtractPreferencesAsync(PreferenceExtractionRequest request, CancellationToken ct)
    {
        var result = await TryPrimaryAsync(
            nameof(ExtractPreferencesAsync),
            () => primary.ExtractPreferencesAsync(request, ct),
            () => fallback.ExtractPreferencesAsync(request, ct),
            ct);

        return Sanitize(result, request);
    }

    public Task<string> GenerateClarificationReplyAsync(ClarificationRequest request, CancellationToken ct) =>
        TryPrimaryAsync(
            nameof(GenerateClarificationReplyAsync),
            () => primary.GenerateClarificationReplyAsync(request, ct),
            () => fallback.GenerateClarificationReplyAsync(request, ct),
            ct);

    public async Task<ItineraryCompositionResult> ComposeItineraryAsync(ItineraryCompositionRequest request, CancellationToken ct)
    {
        var result = await TryPrimaryAsync(
            nameof(ComposeItineraryAsync),
            () => primary.ComposeItineraryAsync(request, ct),
            () => fallback.ComposeItineraryAsync(request, ct),
            ct);

        // Guardrail: si el modelo no eligió NINGÚN candidato real, su respuesta no sirve para componer un
        // itinerario (el Service la descartaría entera y el turista se quedaría sin propuesta). En ese caso
        // se compone con el determinístico, que sólo puede elegir entre los candidatos recibidos.
        var offered = request.CandidateExperiences.Select(e => e.Id)
            .Concat(request.CandidatePackages.Select(p => p.Id))
            .ToHashSet();
        var usable = result.Items.Count(i => offered.Contains(i.ProductId));

        if (usable == 0 && offered.Count > 0 && LastUsedProvider == "primary")
        {
            logger.LogWarning(
                "Ai ComposeItinerary: el modelo no eligió ningún candidato real ({Proposed} propuestos, {Offered} ofrecidos); se compone con el determinístico.",
                result.Items.Count, offered.Count);
            LastUsedProvider = "fallback";
            return await fallback.ComposeItineraryAsync(request, ct);
        }

        if (usable < result.Items.Count)
        {
            logger.LogWarning(
                "Ai ComposeItinerary: se descartaron {Count} ítem(s) con ids que no estaban entre los candidatos ofrecidos.",
                result.Items.Count - usable);
            result = result with { Items = [.. result.Items.Where(i => offered.Contains(i.ProductId))] };
        }

        return result;
    }

    public async Task<ModificationIntentResult> InterpretModificationAsync(ModificationInterpretationRequest request, CancellationToken ct)
    {
        var result = await TryPrimaryAsync(
            nameof(InterpretModificationAsync),
            () => primary.InterpretModificationAsync(request, ct),
            () => fallback.InterpretModificationAsync(request, ct),
            ct);

        // Sólo ids del itinerario vigente y categorías reales; el resto es alucinación y se descarta acá.
        var currentIds = request.CurrentItems.Select(i => i.ItemId).ToHashSet();
        var knownCategories = request.KnownCategoryNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return result with
        {
            TargetItemIds = [.. result.TargetItemIds.Where(currentIds.Contains)],
            AddCategoryNames = [.. result.AddCategoryNames.Where(knownCategories.Contains)],
        };
    }

    public Task<string> GenerateItemExplanationAsync(ItemExplanationRequest request, CancellationToken ct) =>
        TryPrimaryAsync(
            nameof(GenerateItemExplanationAsync),
            () => primary.GenerateItemExplanationAsync(request, ct),
            () => fallback.GenerateItemExplanationAsync(request, ct),
            ct);

    /// <summary>
    /// Sólo se cae al determinístico por fallas del proveedor (caído, timeout, JSON inválido). Cualquier
    /// otra excepción se propaga: esconderla haría invisible un bug nuestro.
    /// </summary>
    private async Task<T> TryPrimaryAsync<T>(string operation, Func<Task<T>> primaryCall, Func<Task<T>> fallbackCall, CancellationToken ct)
    {
        LastUsedProvider = "primary";
        try
        {
            return await primaryCall();
        }
        catch (Exception ex) when (ex is AiModelUnavailableException or AiModelResponseException && !ct.IsCancellationRequested)
        {
            LastUsedProvider = "fallback";
            logger.LogWarning(ex, "Ai {Operation}: el proveedor LLM falló; se resuelve con el cliente determinístico.", operation);
            return await fallbackCall();
        }
    }

    /// <summary>
    /// Guardrail de vocabulario: el modelo sólo puede nombrar destinos y categorías que existen en el
    /// catálogo. Un "Machu Picchu" inventado se descarta acá, antes de llegar al merge de preferencias.
    /// </summary>
    private static PreferenceExtractionResult Sanitize(PreferenceExtractionResult result, PreferenceExtractionRequest request)
    {
        var destinations = request.KnownDestinationNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var categories = request.KnownCategoryNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return result with
        {
            DestinationMention = result.DestinationMention is { } destination && destinations.Contains(destination) ? destination : null,
            CategoryMentions = [.. result.CategoryMentions.Where(categories.Contains).Distinct(StringComparer.OrdinalIgnoreCase)],
            // Una fecha en el pasado no es reservable: se ignora en vez de arrastrarla hasta el booking.
            StartDate = result.StartDate >= request.Today ? result.StartDate : null,
            EndDate = result.EndDate >= request.Today ? result.EndDate : null,
            DurationDays = result.DurationDays is > 0 and <= 90 ? result.DurationDays : null,
            TravelersCount = result.TravelersCount is > 0 and <= 100 ? result.TravelersCount : null,
            BudgetAmount = result.BudgetAmount is > 0 ? result.BudgetAmount : null,
            TravelPaceMention = result.TravelPaceMention is "RELAXED" or "BALANCED" or "INTENSE" ? result.TravelPaceMention : null,
        };
    }
}
