using System.Globalization;
using System.Text.RegularExpressions;

namespace TurisClick.Api.Modules.Ai.Services.LlmClients;

/// <summary>
/// Implementación 100% determinística de IAiModelClient — sin llamar a ningún LLM. Se activa con
/// `Ai:Provider = Deterministic` (appsettings.Testing.json y el valor por defecto en tests/Newman —
/// ver docs de la sesión, sección 18/19: "los tests no deben depender de Ollama real"). Hace NLU
/// simple por reglas (regex/keywords) en vez de simular respuestas canned — es honesto sobre sus
/// límites (no entiende lenguaje natural libre), pero es 100% reproducible.
/// </summary>
public partial class DeterministicAiModelClient : IAiModelClient
{
    public Task<PreferenceExtractionResult> ExtractPreferencesAsync(PreferenceExtractionRequest request, CancellationToken ct)
    {
        var message = request.LatestMessage;

        var destination = request.KnownDestinationNames
            .FirstOrDefault(name => message.Contains(name, StringComparison.OrdinalIgnoreCase));

        var categories = request.KnownCategoryNames
            .Where(name => message.Contains(name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var isoDates = IsoDateRegex().Matches(message)
            .Select(m => DateOnly.ParseExact(m.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture))
            .OrderBy(d => d)
            .ToList();
        DateOnly? startDate = isoDates.Count > 0 ? isoDates[0] : null;
        DateOnly? endDate = isoDates.Count > 1 ? isoDates[1] : null;

        int? durationDays = null;
        var durationMatch = DurationRegex().Match(message);
        if (durationMatch.Success)
            durationDays = int.Parse(durationMatch.Groups[1].Value, CultureInfo.InvariantCulture);

        int? travelers = null;
        var travelersMatch = TravelersRegex().Match(message);
        if (travelersMatch.Success)
            travelers = int.Parse(travelersMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        else if (CoupleRegex().IsMatch(message))
            travelers = 2;

        decimal? budgetAmount = null;
        string? budgetCurrency = null;
        var budgetMatch = BudgetRegex().Match(message);
        var isPerPerson = false;
        if (budgetMatch.Success)
        {
            budgetAmount = decimal.Parse(budgetMatch.Groups["amount"].Value, CultureInfo.InvariantCulture);
            budgetCurrency = budgetMatch.Groups["currency"].Success
                ? NormalizeCurrencyWord(budgetMatch.Groups["currency"].Value)
                : "USD";
            isPerPerson = message.Contains("por persona", StringComparison.OrdinalIgnoreCase);
        }

        return Task.FromResult(new PreferenceExtractionResult(
            destination,
            categories,
            startDate,
            endDate,
            durationDays,
            travelers,
            budgetAmount,
            budgetCurrency,
            isPerPerson,
            RestrictionsNotes: null));
    }

    public Task<string> GenerateClarificationReplyAsync(ClarificationRequest request, CancellationToken ct)
    {
        var fields = string.Join(", ", request.MissingFields);
        return Task.FromResult($"Para armar tu itinerario necesito que me cuentes: {fields}.");
    }

    public Task<ItineraryCompositionResult> ComposeItineraryAsync(ItineraryCompositionRequest request, CancellationToken ct)
    {
        var items = new List<ComposedItem>();

        // UC-AI-05: los días ya ocupados por ítems preservados no se vuelven a llenar — el backend los
        // re-inserta él mismo, acá solo se completa lo que quedó libre (sección 2: "no regenerar lo que
        // el usuario no pidió cambiar").
        var occupiedDays = request.PreservedItems.Select(p => p.DayNumber).ToHashSet();

        var strongPackage = request.CandidatePackages.FirstOrDefault(p => p.IsStrongFit);
        var day = 1;

        // Un Package cubre varios días: solo se propone si NO hay nada preservado (si el turista pidió
        // conservar parte del viaje, meter un paquete de N días encima pisaría esos días).
        if (strongPackage is not null && request.PreservedItems.Count == 0)
        {
            var availability = strongPackage.Availabilities.OrderBy(a => a.Date).FirstOrDefault();
            items.Add(new ComposedItem(day, "PACKAGE", strongPackage.Id, availability?.Id));
            day += strongPackage.DurationDays;
        }

        foreach (var experience in request.CandidateExperiences)
        {
            while (occupiedDays.Contains(day)) day++;
            if (day > request.TripDurationDays) break;

            var availability = experience.Availabilities.OrderBy(a => a.Date).FirstOrDefault();
            items.Add(new ComposedItem(day, "EXPERIENCE", experience.Id, availability?.Id));
            day++;
        }

        var title = request.Preferences.PreferredDestinationName is { } destinationName
            ? $"Tu viaje a {destinationName}"
            : "Tu itinerario personalizado";

        string explanation;
        if (request.ModificationInstruction is not null)
        {
            explanation = request.PreservedItems.Count > 0
                ? $"Ajusté tu itinerario según lo que pediste y mantuve los {request.PreservedItems.Count} componente(s) que no mencionaste."
                : "Rearmé tu itinerario según lo que pediste, con productos reales disponibles.";
        }
        else
        {
            explanation = strongPackage is not null
                ? $"Te recomiendo el paquete \"{strongPackage.Title}\" porque cubre bien lo que buscás, complementado con experiencias adicionales."
                : "Armé un itinerario combinando las experiencias reales disponibles que mejor encajan con lo que pediste.";
        }

        return Task.FromResult(new ItineraryCompositionResult(title, items, explanation));
    }

    /// <summary>
    /// UC-AI-05 determinístico: keywords → acción, y matching por substring contra los TÍTULOS/CATEGORÍAS
    /// reales de los ítems vigentes (nunca inventa ids: solo puede devolver ids que recibió). No pretende
    /// ser NLU real — pretende ser 100% predecible para tests/Newman (sección 12 de la sesión).
    /// </summary>
    public Task<ModificationIntentResult> InterpretModificationAsync(ModificationInterpretationRequest request, CancellationToken ct)
    {
        var message = request.LatestMessage;

        if (request.CurrentItems.Count == 0)
            return Task.FromResult(new ModificationIntentResult(ModificationAction.NONE, [], [], []));

        var targetDays = DayRegex().Matches(message)
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct()
            .ToList();

        // "el último día" no trae número — se resuelve contra el itinerario real, no se adivina.
        if (LastDayRegex().IsMatch(message))
        {
            var lastDay = request.CurrentItems.Max(i => i.DayNumber);
            if (!targetDays.Contains(lastDay)) targetDays.Add(lastDay);
        }

        var mentionedCategories = request.KnownCategoryNames
            .Where(name => message.Contains(name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Ítems mencionados: por categoría real, por título real o por el día que ocupan.
        var matchedByText = request.CurrentItems
            .Where(item =>
                message.Contains(item.Title, StringComparison.OrdinalIgnoreCase)
                || item.CategoryNames.Any(c => message.Contains(c, StringComparison.OrdinalIgnoreCase)))
            .Select(i => i.ItemId)
            .ToList();

        var matchedByDay = request.CurrentItems
            .Where(i => targetDays.Contains(i.DayNumber))
            .Select(i => i.ItemId)
            .ToList();

        var targetItemIds = matchedByText.Concat(matchedByDay).Distinct().ToList();

        if (PreferPackageRegex().IsMatch(message))
            return Task.FromResult(new ModificationIntentResult(ModificationAction.PREFER_PACKAGE, [], [], []));

        if (ReduceBudgetRegex().IsMatch(message))
            return Task.FromResult(new ModificationIntentResult(ModificationAction.REDUCE_BUDGET, [], [], []));

        if (RemoveRegex().IsMatch(message) && targetItemIds.Count > 0)
            return Task.FromResult(new ModificationIntentResult(ModificationAction.REMOVE, targetItemIds, targetDays, []));

        if (AddRegex().IsMatch(message))
            return Task.FromResult(new ModificationIntentResult(ModificationAction.ADD, [], targetDays, mentionedCategories));

        if (ChangeRegex().IsMatch(message) && targetItemIds.Count > 0)
            return Task.FromResult(new ModificationIntentResult(ModificationAction.REPLACE, targetItemIds, targetDays, mentionedCategories));

        return Task.FromResult(new ModificationIntentResult(ModificationAction.NONE, [], [], []));
    }

    /// <summary>
    /// UC-AI-06 determinístico: los hechos ya vienen calculados por el backend desde Postgres, así que
    /// "redactar" acá es concatenarlos. Nunca agrega nada que no esté en Facts.
    /// </summary>
    public Task<string> GenerateItemExplanationAsync(ItemExplanationRequest request, CancellationToken ct)
    {
        var facts = string.Join(" ", request.Facts);
        var intro = $"Te propuse \"{request.Item.Title}\" para el día {request.Item.DayNumber}.";
        return Task.FromResult(string.IsNullOrWhiteSpace(facts) ? intro : $"{intro} {facts}");
    }

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}")]
    private static partial Regex IsoDateRegex();

    [GeneratedRegex(@"(\d+)\s*d[ií]as?")]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"(\d+)\s*(personas?|viajeros?|pax)")]
    private static partial Regex TravelersRegex();

    [GeneratedRegex(@"\b(mi novia|mi novio|mi esposa|mi esposo|en pareja)\b")]
    private static partial Regex CoupleRegex();

    [GeneratedRegex(@"d[ií]a\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex DayRegex();

    [GeneratedRegex(@"[uú]ltimo d[ií]a", RegexOptions.IgnoreCase)]
    private static partial Regex LastDayRegex();

    [GeneratedRegex(@"\b(quit[aá]|saca|sacame|elimin[aá]|borr[aá]|no quiero|sin)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RemoveRegex();

    [GeneratedRegex(@"\b(agreg[aá]|añad[ií]|sum[aá]|met[eé]|quiero m[aá]s|algo m[aá]s)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AddRegex();

    [GeneratedRegex(@"\b(cambi[aá]|reemplaz[aá]|otro|otra|prefiero)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ChangeRegex();

    [GeneratedRegex(@"\b(gastar menos|m[aá]s barato|m[aá]s econ[oó]mico|reduc[ií] el (presupuesto|gasto)|baj[aá] el (precio|presupuesto))\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReduceBudgetRegex();

    [GeneratedRegex(@"\b(prefiero un (package|paquete)|mejor un (package|paquete)|un solo (package|paquete))\b", RegexOptions.IgnoreCase)]
    private static partial Regex PreferPackageRegex();

    /// <summary>
    /// Exige contexto de moneda (símbolo o palabra) para no confundir un monto con cualquier otro
    /// número de la frase (días, viajeros) — los dos grupos "amount" en ramas distintas de la
    /// alternancia son válidos en el motor de regex de .NET.
    /// </summary>
    [GeneratedRegex(@"\$\s*(?<amount>\d+(?:\.\d+)?)|(?<amount>\d+(?:\.\d+)?)\s*(?<currency>USD|BOB|EUR|d[oó]lares|bolivianos)")]
    private static partial Regex BudgetRegex();

    private static string NormalizeCurrencyWord(string word) => word.ToUpperInvariant() switch
    {
        "DÓLARES" or "DOLARES" => "USD",
        "BOLIVIANOS" => "BOB",
        var code => code
    };
}
