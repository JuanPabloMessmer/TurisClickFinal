using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TurisClick.Api.Modules.Ai.Services;
using TurisClick.Api.Modules.Ai.Services.LlmClients;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Ai;

/// <summary>
/// Proveedor Hybrid: las reglas ganan en los escalares que el mensaje escribe literalmente (duración,
/// viajeros, fechas, presupuesto) y el modelo manda en todo lo que requiere entender el idioma. Lo que se
/// verifica acá es el reparto, no la calidad de ninguno de los dos.
/// </summary>
public class HybridAiModelClientTests
{
    private static readonly ExtractedPreferencesSnapshot Empty = new(null, null, null, null, null, null, null, [], null);
    private static readonly string[] Destinations = ["La Paz", "Sucre", "Uyuni"];
    private static readonly string[] Categories = ["Naturaleza", "Gastronomía", "Cultura", "Aventura"];

    private static PreferenceExtractionRequest Request(string message) =>
        new([], message, Empty, Destinations, Categories, new DateOnly(2026, 9, 23));

    private static HybridAiModelClient Build(PreferenceExtractionResult fromModel, out Mock<IAiModelClient> model)
    {
        model = new Mock<IAiModelClient>();
        model.Setup(m => m.ExtractPreferencesAsync(It.IsAny<PreferenceExtractionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fromModel);

        return new HybridAiModelClient(model.Object, new DeterministicAiModelClient(), NullLogger<HybridAiModelClient>.Instance);
    }

    private static PreferenceExtractionResult ModelResult(
        string? destination = null,
        IReadOnlyList<string>? categories = null,
        DateOnly? start = null,
        DateOnly? end = null,
        int? duration = null,
        int? travelers = null,
        decimal? budget = null,
        string? currency = null,
        bool perPerson = false,
        string? pace = null) =>
        new(destination, categories ?? [], start, end, duration, travelers, budget, currency, perPerson, null, pace);

    [Fact]
    public async Task TheRuleWins_WhenTheMessageSpellsTheNumberOut()
    {
        // El mensaje dice "5 días" y "4 personas"; el modelo contó mal.
        var sut = Build(ModelResult(destination: "Sucre", duration: 2, travelers: 1), out _);

        var result = await sut.ExtractPreferencesAsync(Request("Somos 4 personas y queremos 5 días en Sucre."), CancellationToken.None);

        Assert.Equal(5, result.DurationDays);
        Assert.Equal(4, result.TravelersCount);
        Assert.Equal("Sucre", result.DestinationMention); // el destino sigue viniendo del modelo
    }

    [Fact]
    public async Task TheModelWins_WhenTheRulesFindNothing()
    {
        // Inglés: las reglas no entienden "for 4 days with my girlfriend", el modelo sí.
        var sut = Build(ModelResult(destination: "La Paz", categories: ["Naturaleza"], duration: 4, travelers: 2), out _);

        var result = await sut.ExtractPreferencesAsync(
            Request("I'm going to La Paz for 4 days with my girlfriend, we like nature."), CancellationToken.None);

        Assert.Equal(4, result.DurationDays);
        Assert.Equal(2, result.TravelersCount);
        Assert.Equal(["Naturaleza"], result.CategoryMentions);
    }

    [Fact]
    public async Task TheBudgetTravelsAsOneBlock()
    {
        // Las reglas leen "800 bolivianos": importe, moneda y "por persona" se toman juntos, para no
        // mezclar el monto de una lectura con la moneda de la otra.
        var sut = Build(ModelResult(budget: 500, currency: "USD", perPerson: true), out _);

        var result = await sut.ExtractPreferencesAsync(Request("Tengo 800 bolivianos."), CancellationToken.None);

        Assert.Equal(800, result.BudgetAmount);
        Assert.Equal("BOB", result.BudgetCurrency);
        Assert.False(result.BudgetIsPerPerson);
    }

    [Fact]
    public async Task TheDatesTravelAsOneBlock()
    {
        var sut = Build(ModelResult(start: new DateOnly(2026, 1, 1), end: new DateOnly(2026, 1, 2)), out _);

        var result = await sut.ExtractPreferencesAsync(
            Request("Del 2026-11-10 al 2026-11-14 quiero estar en Sucre."), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 11, 10), result.StartDate);
        Assert.Equal(new DateOnly(2026, 11, 14), result.EndDate);
    }

    [Fact]
    public async Task ThePaceAndTheInterestsStayWithTheModel()
    {
        var sut = Build(ModelResult(categories: ["Cultura"], pace: "RELAXED"), out _);

        var result = await sut.ExtractPreferencesAsync(Request("Something calm and cultural, please."), CancellationToken.None);

        Assert.Equal("RELAXED", result.TravelPaceMention);
        Assert.Equal(["Cultura"], result.CategoryMentions);
    }

    [Fact]
    public async Task EverythingThatIsNotExtraction_IsDelegatedUntouched()
    {
        var sut = Build(ModelResult(), out var model);
        model.Setup(m => m.GenerateClarificationReplyAsync(It.IsAny<ClarificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("¿A dónde querés ir?");
        model.Setup(m => m.InterpretModificationAsync(It.IsAny<ModificationInterpretationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModificationIntentResult(ModificationAction.REMOVE, [], [], []));
        model.Setup(m => m.GenerateItemExplanationAsync(It.IsAny<ItemExplanationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Porque encaja con lo que pediste.");

        Assert.Equal("¿A dónde querés ir?", await sut.GenerateClarificationReplyAsync(
            new ClarificationRequest([], "hola", ["destino"]), CancellationToken.None));

        var modification = await sut.InterpretModificationAsync(
            new ModificationInterpretationRequest([], "sacá eso", Empty, [], Categories), CancellationToken.None);
        Assert.Equal(ModificationAction.REMOVE, modification.Action);

        var explanation = await sut.GenerateItemExplanationAsync(
            new ItemExplanationRequest(Empty, new CurrentItineraryItemView(Guid.NewGuid(), 1, "EXPERIENCE", "Sucre a pie", ["Cultura"], 120, "BOB", null), []),
            CancellationToken.None);
        Assert.Equal("Porque encaja con lo que pediste.", explanation);
    }
}
