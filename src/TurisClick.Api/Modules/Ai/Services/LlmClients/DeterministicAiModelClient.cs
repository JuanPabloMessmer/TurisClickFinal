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

        var strongPackage = request.CandidatePackages.FirstOrDefault(p => p.IsStrongFit);
        var day = 1;

        if (strongPackage is not null)
        {
            var availability = strongPackage.Availabilities.OrderBy(a => a.Date).FirstOrDefault();
            items.Add(new ComposedItem(day, "PACKAGE", strongPackage.Id, availability?.Id));
            day += strongPackage.DurationDays;
        }

        foreach (var experience in request.CandidateExperiences)
        {
            if (day > request.TripDurationDays) break;

            var availability = experience.Availabilities.OrderBy(a => a.Date).FirstOrDefault();
            items.Add(new ComposedItem(day, "EXPERIENCE", experience.Id, availability?.Id));
            day++;
        }

        var title = request.Preferences.PreferredDestinationName is { } destinationName
            ? $"Tu viaje a {destinationName}"
            : "Tu itinerario personalizado";

        var explanation = strongPackage is not null
            ? $"Te recomiendo el paquete \"{strongPackage.Title}\" porque cubre bien lo que buscás, complementado con experiencias adicionales."
            : "Armé un itinerario combinando las experiencias reales disponibles que mejor encajan con lo que pediste.";

        return Task.FromResult(new ItineraryCompositionResult(title, items, explanation));
    }

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}")]
    private static partial Regex IsoDateRegex();

    [GeneratedRegex(@"(\d+)\s*d[ií]as?")]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"(\d+)\s*(personas?|viajeros?|pax)")]
    private static partial Regex TravelersRegex();

    [GeneratedRegex(@"\b(mi novia|mi novio|mi esposa|mi esposo|en pareja)\b")]
    private static partial Regex CoupleRegex();

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
