using Microsoft.Extensions.Logging;
using Moq;
using TurisClick.Api.Modules.Ai.Services;
using TurisClick.Api.Modules.Ai.Services.LlmClients;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Ai;

/// <summary>
/// El LLM nunca puede ser un punto único de falla ni una fuente de verdad. Estos tests fijan las dos
/// garantías del decorador: si el modelo falla, responde el cliente determinístico; y lo que el modelo
/// invente (destinos, categorías, ids, fechas pasadas) se descarta antes de llegar al dominio.
/// </summary>
public class FallbackAiModelClientTests
{
    private static readonly DateOnly Today = new(2026, 9, 27);
    private static readonly ExtractedPreferencesSnapshot EmptySnapshot = new(null, null, null, null, null, null, null, [], null);
    private static readonly string[] KnownDestinations = ["La Paz", "Sucre", "Uyuni"];
    private static readonly string[] KnownCategories = ["Naturaleza", "Aventura", "Cultura", "Gastronomía"];

    private readonly Mock<IAiModelClient> _llm = new();
    private readonly DeterministicAiModelClient _deterministic = new();
    private readonly FallbackAiModelClient _sut;

    public FallbackAiModelClientTests()
    {
        _sut = new FallbackAiModelClient(_llm.Object, _deterministic, Mock.Of<ILogger<FallbackAiModelClient>>());
    }

    private static PreferenceExtractionRequest Extraction(string message) =>
        new([], message, EmptySnapshot, KnownDestinations, KnownCategories, Today);

    private static PreferenceExtractionResult Signals(
        string? destination = null, IReadOnlyList<string>? categories = null, DateOnly? startDate = null,
        int? durationDays = null, int? travelers = null, decimal? budget = null, string? pace = null) =>
        new(destination, categories ?? [], startDate, null, durationDays, travelers, budget, "BOB", false, null, pace);

    // ---------------- Fallback por fallas del proveedor ----------------

    [Theory]
    [MemberData(nameof(ProviderFailures))]
    public async Task ExtractPreferences_WhenTheModelFails_TheDeterministicClientAnswers(Exception failure)
    {
        _llm.Setup(c => c.ExtractPreferencesAsync(It.IsAny<PreferenceExtractionRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure);

        var result = await _sut.ExtractPreferencesAsync(Extraction("Voy 3 días a Sucre, me gusta la cultura"), CancellationToken.None);

        Assert.Equal("fallback", _sut.LastUsedProvider);
        Assert.Equal("Sucre", result.DestinationMention);
        Assert.Equal(3, result.DurationDays);
        Assert.Contains("Cultura", result.CategoryMentions);
    }

    public static TheoryData<Exception> ProviderFailures =>
    [
        new AiModelUnavailableException("Ollama apagado"),          // servicio caído
        new AiModelUnavailableException("timeout"),                  // se pasó del tiempo
        new AiModelResponseException("JSON inválido tras reintento"), // respondió cualquier cosa
    ];

    [Fact]
    public async Task Clarification_And_Explanation_AlsoFallBack()
    {
        _llm.Setup(c => c.GenerateClarificationReplyAsync(It.IsAny<ClarificationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiModelUnavailableException("apagado"));
        _llm.Setup(c => c.GenerateItemExplanationAsync(It.IsAny<ItemExplanationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiModelResponseException("vacío"));

        var reply = await _sut.GenerateClarificationReplyAsync(new ClarificationRequest([], "hola", ["destino"]), CancellationToken.None);
        var item = new CurrentItineraryItemView(Guid.NewGuid(), 1, "EXPERIENCE", "Tour", ["Cultura"], 120, "BOB", null);
        var explanation = await _sut.GenerateItemExplanationAsync(new ItemExplanationRequest(EmptySnapshot, item, ["Quedan 5 lugares."]), CancellationToken.None);

        Assert.Contains("destino", reply);
        Assert.Contains("Quedan 5 lugares.", explanation);
    }

    [Fact]
    public async Task AnUnexpectedErrorIsNotSwallowed()
    {
        _llm.Setup(c => c.ExtractPreferencesAsync(It.IsAny<PreferenceExtractionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bug nuestro"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ExtractPreferencesAsync(Extraction("hola"), CancellationToken.None));
    }

    [Fact]
    public async Task WhenTheModelAnswers_ItsResultIsUsed()
    {
        _llm.Setup(c => c.ExtractPreferencesAsync(It.IsAny<PreferenceExtractionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Signals(destination: "Uyuni", categories: ["Aventura"], travelers: 2));

        var result = await _sut.ExtractPreferencesAsync(Extraction("algo lindo"), CancellationToken.None);

        Assert.Equal("primary", _sut.LastUsedProvider);
        Assert.Equal("Uyuni", result.DestinationMention);
        Assert.Equal(2, result.TravelersCount);
    }

    // ---------------- Guardrails sobre lo que devuelve el modelo ----------------

    [Fact]
    public async Task InventedDestinationsAndCategoriesAreDropped()
    {
        _llm.Setup(c => c.ExtractPreferencesAsync(It.IsAny<PreferenceExtractionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Signals(destination: "Machu Picchu", categories: ["Aventura", "Buceo", "Aventura"]));

        var result = await _sut.ExtractPreferencesAsync(Extraction("Machu Picchu y buceo"), CancellationToken.None);

        Assert.Null(result.DestinationMention);
        Assert.Equal(["Aventura"], result.CategoryMentions);
    }

    [Theory]
    [InlineData(-1, null)]      // duración negativa
    [InlineData(0, null)]       // cero días
    [InlineData(400, null)]     // más de un año
    [InlineData(5, 5)]          // razonable
    public async Task AbsurdDurationsAreDropped(int durationDays, int? expected)
    {
        _llm.Setup(c => c.ExtractPreferencesAsync(It.IsAny<PreferenceExtractionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Signals(durationDays: durationDays));

        var result = await _sut.ExtractPreferencesAsync(Extraction("x"), CancellationToken.None);

        Assert.Equal(expected, result.DurationDays);
    }

    [Fact]
    public async Task PastDatesAndImpossibleNumbersAreDropped()
    {
        _llm.Setup(c => c.ExtractPreferencesAsync(It.IsAny<PreferenceExtractionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Signals(startDate: Today.AddDays(-10), travelers: 0, budget: -50, pace: "TURBO"));

        var result = await _sut.ExtractPreferencesAsync(Extraction("x"), CancellationToken.None);

        Assert.Null(result.StartDate);
        Assert.Null(result.TravelersCount);
        Assert.Null(result.BudgetAmount);
        Assert.Null(result.TravelPaceMention);
    }

    // ---------------- Composición: ids inventados ----------------

    private static ItineraryCompositionRequest Composition(params CandidateExperience[] candidates) =>
        new(EmptySnapshot, 2, candidates, [], [], null, null);

    private static CandidateExperience Candidate(Guid id) =>
        new(id, $"Tour {id:N}"[..10], "Sucre", 150, "BOB", ["Cultura"], 120, [new CandidateAvailability(Guid.NewGuid(), Today.AddDays(10), 8)]);

    [Fact]
    public async Task ComposeItinerary_AllIdsInvented_FallsBackToTheDeterministicComposition()
    {
        var real = Candidate(Guid.NewGuid());
        _llm.Setup(c => c.ComposeItineraryAsync(It.IsAny<ItineraryCompositionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItineraryCompositionResult("Viaje", [new ComposedItem(1, "EXPERIENCE", Guid.NewGuid(), null)], "inventado"));

        var result = await _sut.ComposeItineraryAsync(Composition(real), CancellationToken.None);

        Assert.Equal("fallback", _sut.LastUsedProvider);
        Assert.Equal(real.Id, Assert.Single(result.Items).ProductId);
    }

    [Fact]
    public async Task ComposeItinerary_MixedIds_KeepsOnlyTheRealOnes()
    {
        var real = Candidate(Guid.NewGuid());
        _llm.Setup(c => c.ComposeItineraryAsync(It.IsAny<ItineraryCompositionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItineraryCompositionResult(
                "Viaje",
                [new ComposedItem(1, "EXPERIENCE", real.Id, null), new ComposedItem(2, "EXPERIENCE", Guid.NewGuid(), null)],
                "mitad y mitad"));

        var result = await _sut.ComposeItineraryAsync(Composition(real), CancellationToken.None);

        Assert.Equal("primary", _sut.LastUsedProvider);
        Assert.Equal(real.Id, Assert.Single(result.Items).ProductId);
        Assert.Equal("mitad y mitad", result.AssistantExplanation);
    }

    [Fact]
    public async Task ComposeItinerary_WhenTheModelIsDown_TheDeterministicComposes()
    {
        var real = Candidate(Guid.NewGuid());
        _llm.Setup(c => c.ComposeItineraryAsync(It.IsAny<ItineraryCompositionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiModelUnavailableException("apagado"));

        var result = await _sut.ComposeItineraryAsync(Composition(real), CancellationToken.None);

        Assert.Equal(real.Id, Assert.Single(result.Items).ProductId);
    }

    // ---------------- Modificaciones: ids y categorías ajenas ----------------

    [Fact]
    public async Task InterpretModification_DropsItemIdsAndCategoriesThatDoNotExist()
    {
        var item = new CurrentItineraryItemView(Guid.NewGuid(), 1, "EXPERIENCE", "Rafting", ["Aventura"], 300, "BOB", null);
        _llm.Setup(c => c.InterpretModificationAsync(It.IsAny<ModificationInterpretationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModificationIntentResult(ModificationAction.REMOVE, [item.ItemId, Guid.NewGuid()], [1], ["Aventura", "Parapente"]));

        var result = await _sut.InterpretModificationAsync(
            new ModificationInterpretationRequest([], "sacá el rafting", EmptySnapshot, [item], KnownCategories),
            CancellationToken.None);

        Assert.Equal([item.ItemId], result.TargetItemIds);
        Assert.Equal(["Aventura"], result.AddCategoryNames);
    }

    [Fact]
    public async Task InterpretModification_WhenTheModelFails_TheDeterministicInterprets()
    {
        var item = new CurrentItineraryItemView(Guid.NewGuid(), 2, "EXPERIENCE", "Rafting", ["Aventura"], 300, "BOB", null);
        _llm.Setup(c => c.InterpretModificationAsync(It.IsAny<ModificationInterpretationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiModelResponseException("JSON inválido"));

        var result = await _sut.InterpretModificationAsync(
            new ModificationInterpretationRequest([], "Quiero algo más barato", EmptySnapshot, [item], KnownCategories),
            CancellationToken.None);

        Assert.Equal(ModificationAction.REDUCE_BUDGET, result.Action);
    }
}
