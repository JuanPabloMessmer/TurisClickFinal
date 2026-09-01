using TurisClick.Api.Modules.Ai.Services;
using TurisClick.Api.Modules.Ai.Services.LlmClients;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Ai;

/// <summary>UC-AI-01 — extracción de preferencias 100% determinística (sin LLM real), usada por tests/Newman.</summary>
public class DeterministicAiModelClientTests
{
    private readonly DeterministicAiModelClient _sut = new();

    private static readonly ExtractedPreferencesSnapshot EmptySnapshot = new(null, null, null, null, null, null, null, [], null);

    private static readonly string[] KnownDestinations = ["Uyuni", "La Paz", "Sucre"];
    private static readonly string[] KnownCategories = ["Aventura", "Naturaleza", "Gastronomía", "Cultura"];

    private PreferenceExtractionRequest Request(string message) =>
        new([], message, EmptySnapshot, KnownDestinations, KnownCategories, new DateOnly(2026, 8, 30));

    [Fact]
    public async Task ExtractPreferences_RecognizesKnownDestination()
    {
        var result = await _sut.ExtractPreferencesAsync(Request("Queremos conocer Uyuni con mi novia."), CancellationToken.None);

        Assert.Equal("Uyuni", result.DestinationMention);
    }

    [Fact]
    public async Task ExtractPreferences_UnknownDestination_ReturnsNull()
    {
        var result = await _sut.ExtractPreferencesAsync(Request("Queremos ir a Machu Picchu."), CancellationToken.None);

        Assert.Null(result.DestinationMention);
    }

    [Fact]
    public async Task ExtractPreferences_RecognizesKnownCategories()
    {
        var result = await _sut.ExtractPreferencesAsync(
            Request("Nos gusta la naturaleza, la gastronomía y la aventura."), CancellationToken.None);

        Assert.Contains("Naturaleza", result.CategoryMentions);
        Assert.Contains("Gastronomía", result.CategoryMentions);
        Assert.Contains("Aventura", result.CategoryMentions);
        Assert.DoesNotContain("Cultura", result.CategoryMentions);
    }

    [Fact]
    public async Task ExtractPreferences_ParsesIsoDateRange()
    {
        var result = await _sut.ExtractPreferencesAsync(Request("Del 2026-09-10 al 2026-09-15."), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 9, 10), result.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 15), result.EndDate);
    }

    [Fact]
    public async Task ExtractPreferences_ParsesTravelersFromExplicitNumber()
    {
        var result = await _sut.ExtractPreferencesAsync(Request("Somos 3 personas."), CancellationToken.None);

        Assert.Equal(3, result.TravelersCount);
    }

    [Fact]
    public async Task ExtractPreferences_ParsesTravelersFromCoupleMention()
    {
        var result = await _sut.ExtractPreferencesAsync(Request("Voy con mi novia."), CancellationToken.None);

        Assert.Equal(2, result.TravelersCount);
    }

    [Fact]
    public async Task ExtractPreferences_ParsesDurationDays()
    {
        var result = await _sut.ExtractPreferencesAsync(Request("Queremos un viaje de 5 días."), CancellationToken.None);

        Assert.Equal(5, result.DurationDays);
    }

    [Fact]
    public async Task ExtractPreferences_ParsesBudgetWithDollarSign_AsPerPerson()
    {
        var result = await _sut.ExtractPreferencesAsync(Request("Tenemos unos $500 por persona."), CancellationToken.None);

        Assert.Equal(500, result.BudgetAmount);
        Assert.Equal("USD", result.BudgetCurrency);
        Assert.True(result.BudgetIsPerPerson);
    }

    [Fact]
    public async Task ExtractPreferences_ParsesBudgetWithCurrencyWord_NotPerPerson()
    {
        var result = await _sut.ExtractPreferencesAsync(Request("Nuestro presupuesto total es de 1200 bolivianos."), CancellationToken.None);

        Assert.Equal(1200, result.BudgetAmount);
        Assert.Equal("BOB", result.BudgetCurrency);
        Assert.False(result.BudgetIsPerPerson);
    }

    [Fact]
    public async Task ExtractPreferences_PlainNumberWithoutCurrencyContext_IsNotTreatedAsBudget()
    {
        // "5 días" y "3 personas" no deberían confundirse con un monto de presupuesto.
        var result = await _sut.ExtractPreferencesAsync(Request("Vamos 3 personas por 5 días."), CancellationToken.None);

        Assert.Null(result.BudgetAmount);
    }

    [Fact]
    public async Task GenerateClarificationReply_MentionsOnlyMissingFields()
    {
        var reply = await _sut.GenerateClarificationReplyAsync(
            new ClarificationRequest([], "Quiero ir a Uyuni.", ["fechas o duración del viaje", "cantidad de viajeros"]), CancellationToken.None);

        Assert.Contains("fechas o duración del viaje", reply);
        Assert.Contains("cantidad de viajeros", reply);
    }

    [Fact]
    public async Task ComposeItinerary_PrefersStrongFitPackageAsBase()
    {
        var packageId = Guid.NewGuid();
        var experienceId = Guid.NewGuid();
        var request = new ItineraryCompositionRequest(
            EmptySnapshot,
            TripDurationDays: 5,
            CandidateExperiences: [new CandidateExperience(experienceId, "Cementerio de Trenes", "Uyuni", 20, "USD", [], null, [])],
            CandidatePackages: [new CandidatePackage(packageId, "Uyuni 3 días", "Uyuni", 300, "USD", 3, [], [], IsStrongFit: true)]);

        var result = await _sut.ComposeItineraryAsync(request, CancellationToken.None);

        var packageItem = Assert.Single(result.Items, i => i.ProductType == "PACKAGE");
        Assert.Equal(packageId, packageItem.ProductId);
        Assert.Equal(1, packageItem.DayNumber);
        // El paquete ocupa los días 1-3; la experiencia debería completar a partir del día 4.
        var experienceItem = Assert.Single(result.Items, i => i.ProductType == "EXPERIENCE");
        Assert.Equal(4, experienceItem.DayNumber);
    }
}
