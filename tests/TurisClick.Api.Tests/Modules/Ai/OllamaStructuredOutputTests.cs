using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using TurisClick.Api.Modules.Ai.Services;
using TurisClick.Api.Modules.Ai.Services.LlmClients;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Ai;

/// <summary>
/// Salida estructurada (Fase 6): al modelo se le pide la forma exacta con un JSON Schema y temperatura 0,
/// y aun así el cliente tolera lo que los modelos chicos suelen hacer (envolver el JSON en ```json). Nunca
/// se pega a un Ollama real: el HttpMessageHandler es falso.
/// </summary>
public class OllamaStructuredOutputTests
{
    private static readonly ExtractedPreferencesSnapshot EmptySnapshot = new(null, null, null, null, null, null, null, [], null);

    private static readonly string ValidExtraction =
        """{"destination":"Sucre","categories":["Cultura"],"startDate":null,"endDate":null,"durationDays":3,"travelers":null,"budgetAmount":null,"budgetCurrency":null,"budgetIsPerPerson":false,"restrictionsNotes":null,"travelPace":"RELAXED"}""";

    private static OllamaAiModelClient Build(HttpMessageHandler handler, OllamaOptions? ollama = null) =>
        new(new HttpClient(handler),
            Options.Create(new AiOptions { Ollama = ollama ?? new OllamaOptions { BaseUrl = "http://fake-ollama:11434", Model = "test-model", TimeoutSeconds = 5 } }),
            Mock.Of<ILogger<OllamaAiModelClient>>());

    private static HttpResponseMessage Ok(string modelText) => new(HttpStatusCode.OK)
    {
        Content = new StringContent($$"""{"response": {{JsonSerializer.Serialize(modelText)}} }""", Encoding.UTF8, "application/json"),
    };

    private static PreferenceExtractionRequest Request() =>
        new([], "Tres días tranquilos en Sucre", EmptySnapshot, ["Sucre"], ["Cultura"], new DateOnly(2026, 9, 27));

    [Fact]
    public async Task TheRequestCarriesTheSchema_Temperature0_AndNoStreaming()
    {
        string? body = null;
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage request, CancellationToken _) =>
            {
                body = await request.Content!.ReadAsStringAsync();
                return Ok(ValidExtraction);
            });

        await Build(handler.Object).ExtractPreferencesAsync(Request(), CancellationToken.None);

        using var sent = JsonDocument.Parse(body!);
        var root = sent.RootElement;
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal(0, root.GetProperty("options").GetProperty("temperature").GetDouble());
        // `format` es el esquema, no la cadena "json": el modelo devuelve la forma exacta que esperamos.
        var format = root.GetProperty("format");
        Assert.Equal(JsonValueKind.Object, format.ValueKind);
        Assert.True(format.GetProperty("properties").TryGetProperty("travelPace", out _));
    }

    [Fact]
    public async Task IfTheServerRejectsTheSchema_ItRetriesWithPlainJsonMode()
    {
        var formats = new List<JsonValueKind>();
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage request, CancellationToken _) =>
            {
                using var sent = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                var kind = sent.RootElement.GetProperty("format").ValueKind;
                formats.Add(kind);
                return kind == JsonValueKind.Object ? new HttpResponseMessage(HttpStatusCode.BadRequest) : Ok(ValidExtraction);
            });

        var result = await Build(handler.Object).ExtractPreferencesAsync(Request(), CancellationToken.None);

        Assert.Equal([JsonValueKind.Object, JsonValueKind.String], formats);
        Assert.Equal("Sucre", result.DestinationMention);
    }

    [Fact]
    public async Task SchemaCanBeTurnedOff()
    {
        JsonValueKind? format = null;
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage request, CancellationToken _) =>
            {
                using var sent = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                format = sent.RootElement.GetProperty("format").ValueKind;
                return Ok(ValidExtraction);
            });

        await Build(handler.Object, new OllamaOptions { BaseUrl = "http://fake-ollama:11434", Model = "m", TimeoutSeconds = 5, UseJsonSchema = false })
            .ExtractPreferencesAsync(Request(), CancellationToken.None);

        Assert.Equal(JsonValueKind.String, format);
    }

    [Theory]
    [InlineData("```json\n{JSON}\n```")]
    [InlineData("Claro, acá tenés el resultado:\n{JSON}")]
    [InlineData("{JSON}\n\nEspero que te sirva.")]
    public async Task JsonWrappedInProseOrFencesIsStillParsed(string template)
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => Ok(template.Replace("{JSON}", ValidExtraction)));

        var result = await Build(handler.Object).ExtractPreferencesAsync(Request(), CancellationToken.None);

        Assert.Equal("Sucre", result.DestinationMention);
        Assert.Equal("RELAXED", result.TravelPaceMention);
        // Una sola llamada: no hizo falta el reintento por formato.
        handler.Protected().Verify("SendAsync", Times.Once(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task ATimeoutBecomesModelUnavailable_NotACrash()
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage _, CancellationToken token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return Ok(ValidExtraction);
            });

        var client = Build(handler.Object, new OllamaOptions { BaseUrl = "http://fake-ollama:11434", Model = "m", TimeoutSeconds = 1 });

        await Assert.ThrowsAsync<AiModelUnavailableException>(() => client.ExtractPreferencesAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task AnUnreachableServerBecomesModelUnavailable()
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<AiModelUnavailableException>(() =>
            Build(handler.Object).ExtractPreferencesAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task ThePromptNeverCarriesSecretsOrInternalConfiguration()
    {
        string? body = null;
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage request, CancellationToken _) =>
            {
                body = await request.Content!.ReadAsStringAsync();
                return Ok(ValidExtraction);
            });

        await Build(handler.Object).ExtractPreferencesAsync(Request(), CancellationToken.None);

        foreach (var forbidden in new[] { "ConnectionStrings", "Password=", "Jwt", "Bearer", "azurewebsites", "postgres" })
            Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
    }
}
