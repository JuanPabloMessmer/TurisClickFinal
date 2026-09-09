using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using TurisClick.Api.Modules.Ai.Services;
using TurisClick.Api.Modules.Ai.Services.LlmClients;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Ai;

/// <summary>
/// Sección 7 de la sesión: "si el LLM devuelve JSON inválido, no crash, retry controlado, error
/// funcional". Estos tests simulan las respuestas HTTP de Ollama con un HttpMessageHandler fake — nunca
/// pegan a un Ollama real.
/// </summary>
public class OllamaAiModelClientTests
{
    private static readonly ExtractedPreferencesSnapshot EmptySnapshot = new(null, null, null, null, null, null, null, [], null);

    private static (OllamaAiModelClient Client, Mock<HttpMessageHandler> Handler) BuildClient(params string[] responseBodies)
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var setup = handler.Protected().SetupSequence<Task<HttpResponseMessage>>(
            "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());

        foreach (var body in responseBodies)
        {
            setup = setup.ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"response": {{System.Text.Json.JsonSerializer.Serialize(body)}} }""", Encoding.UTF8, "application/json")
            });
        }

        var httpClient = new HttpClient(handler.Object);
        var options = Options.Create(new AiOptions { Ollama = new OllamaOptions { BaseUrl = "http://fake-ollama:11434", Model = "test-model", TimeoutSeconds = 5 } });
        var client = new OllamaAiModelClient(httpClient, options, Mock.Of<ILogger<OllamaAiModelClient>>());

        return (client, handler);
    }

    [Fact]
    public async Task ExtractPreferences_ValidJson_ParsesCorrectly()
    {
        const string modelJson = """{"destination":"Uyuni","categories":["Aventura"],"startDate":null,"endDate":null,"durationDays":3,"travelers":2,"budgetAmount":500,"budgetCurrency":"USD","budgetIsPerPerson":true,"restrictionsNotes":null}""";
        var (client, _) = BuildClient(modelJson);

        var result = await client.ExtractPreferencesAsync(
            new PreferenceExtractionRequest([], "Uyuni, 3 días, 2 personas", EmptySnapshot, ["Uyuni"], ["Aventura"], new DateOnly(2026, 8, 30)),
            CancellationToken.None);

        Assert.Equal("Uyuni", result.DestinationMention);
        Assert.Equal(3, result.DurationDays);
        Assert.Equal(2, result.TravelersCount);
        Assert.True(result.BudgetIsPerPerson);
    }

    [Fact]
    public async Task ExtractPreferences_InvalidJsonOnce_RetriesAndSucceeds()
    {
        const string invalidJson = "this is not json at all";
        const string validJson = """{"destination":null,"categories":[],"startDate":null,"endDate":null,"durationDays":null,"travelers":null,"budgetAmount":null,"budgetCurrency":null,"budgetIsPerPerson":false,"restrictionsNotes":null}""";
        var (client, handler) = BuildClient(invalidJson, validJson);

        var result = await client.ExtractPreferencesAsync(
            new PreferenceExtractionRequest([], "hola", EmptySnapshot, [], [], new DateOnly(2026, 8, 30)),
            CancellationToken.None);

        Assert.Null(result.DestinationMention);
        handler.Protected().Verify(
            "SendAsync", Times.Exactly(2), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task ExtractPreferences_InvalidJsonTwice_ThrowsAiModelResponseException_NoCrash()
    {
        var (client, _) = BuildClient("not json", "still not json");

        await Assert.ThrowsAsync<AiModelResponseException>(() =>
            client.ExtractPreferencesAsync(
                new PreferenceExtractionRequest([], "hola", EmptySnapshot, [], [], new DateOnly(2026, 8, 30)),
                CancellationToken.None));
    }

    [Fact]
    public async Task InterpretModification_ParsesActionAndTargetIds()
    {
        var itemId = Guid.NewGuid();
        var modelJson = $$"""{"action":"REMOVE","targetItemIds":["{{itemId}}"],"targetDays":[2],"addCategories":[]}""";
        var (client, _) = BuildClient(modelJson);

        var result = await client.InterpretModificationAsync(
            new ModificationInterpretationRequest([], "Quitá el rafting", EmptySnapshot,
                [new CurrentItineraryItemView(itemId, 2, "EXPERIENCE", "Rafting", ["Aventura"], 60, "USD", null)], ["Aventura"]),
            CancellationToken.None);

        Assert.Equal(ModificationAction.REMOVE, result.Action);
        Assert.Equal(itemId, Assert.Single(result.TargetItemIds));
        Assert.Equal(2, Assert.Single(result.TargetDays));
    }

    [Fact]
    public async Task InterpretModification_UnknownAction_FallsBackToNone()
    {
        // Si el modelo inventa una acción que no existe, no se rompe ni se adivina: se trata como
        // "no es un ajuste", que es el camino seguro.
        var (client, _) = BuildClient("""{"action":"EXPLOTAR_TODO","targetItemIds":[],"targetDays":[],"addCategories":[]}""");

        var result = await client.InterpretModificationAsync(
            new ModificationInterpretationRequest([], "hola", EmptySnapshot,
                [new CurrentItineraryItemView(Guid.NewGuid(), 1, "EXPERIENCE", "Rafting", [], 60, "USD", null)], []),
            CancellationToken.None);

        Assert.Equal(ModificationAction.NONE, result.Action);
    }

    [Fact]
    public async Task InterpretModification_WithoutCurrentItems_DoesNotCallTheModel()
    {
        var (client, handler) = BuildClient();

        var result = await client.InterpretModificationAsync(
            new ModificationInterpretationRequest([], "Quitá algo", EmptySnapshot, [], []),
            CancellationToken.None);

        Assert.Equal(ModificationAction.NONE, result.Action);
        handler.Protected().Verify("SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GenerateItemExplanation_ReturnsExplanationText()
    {
        var (client, _) = BuildClient("""{"explanation":"Te lo propuse porque coincide con tus intereses."}""");

        var result = await client.GenerateItemExplanationAsync(
            new ItemExplanationRequest(EmptySnapshot,
                new CurrentItineraryItemView(Guid.NewGuid(), 1, "EXPERIENCE", "Salar", ["Naturaleza"], 80, "USD", null),
                ["Coincide con los intereses que indicaste: Naturaleza."]),
            CancellationToken.None);

        Assert.Contains("intereses", result);
    }

    [Fact]
    public async Task GenerateItemExplanation_InvalidJsonTwice_ThrowsInsteadOfInventing()
    {
        var (client, _) = BuildClient("no json", "tampoco");

        await Assert.ThrowsAsync<AiModelResponseException>(() =>
            client.GenerateItemExplanationAsync(
                new ItemExplanationRequest(EmptySnapshot,
                    new CurrentItineraryItemView(Guid.NewGuid(), 1, "EXPERIENCE", "Salar", [], 80, "USD", null), []),
                CancellationToken.None));
    }

    [Fact]
    public async Task ExtractPreferences_OllamaUnreachable_ThrowsAiModelUnavailableException_NoCrash()
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var httpClient = new HttpClient(handler.Object);
        var options = Options.Create(new AiOptions { Ollama = new OllamaOptions { BaseUrl = "http://fake-ollama:11434", Model = "test-model", TimeoutSeconds = 5 } });
        var client = new OllamaAiModelClient(httpClient, options, Mock.Of<ILogger<OllamaAiModelClient>>());

        await Assert.ThrowsAsync<AiModelUnavailableException>(() =>
            client.ExtractPreferencesAsync(
                new PreferenceExtractionRequest([], "hola", EmptySnapshot, [], [], new DateOnly(2026, 8, 30)),
                CancellationToken.None));
    }
}
