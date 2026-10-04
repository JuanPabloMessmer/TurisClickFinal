using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TurisClick.Api.Modules.Ai;
using TurisClick.Api.Modules.Ai.Services;
using TurisClick.Api.Modules.Ai.Services.LlmClients;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Ai;

/// <summary>
/// El proveedor de IA se elige por configuración y nada más del backend cambia (fase 4 del plan).
/// Esto lo verifica desde el contenedor: qué implementación de IAiModelClient sale con cada valor de
/// Ai:Provider, sin tocar controllers ni services.
/// </summary>
public class AiProviderWiringTests
{
    private static IAiModelClient Resolve(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        // Sólo se resuelve IAiModelClient: los clientes no dependen de la base, así que no hace falta
        // registrar el DbContext para comprobar el cableado.
        services.AddAiModule(configuration);

        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IAiModelClient>();
    }

    [Fact]
    public void WithoutConfiguration_TheDeterministicClientIsTheDefault()
    {
        Assert.IsType<DeterministicAiModelClient>(Resolve());
    }

    [Fact]
    public void Deterministic_ResolvesTheRuleBasedClient()
    {
        Assert.IsType<DeterministicAiModelClient>(Resolve(("Ai:Provider", "Deterministic")));
    }

    [Fact]
    public void Ollama_ResolvesTheLlmWrappedInTheFallbackDecorator()
    {
        Assert.IsType<FallbackAiModelClient>(Resolve(("Ai:Provider", "Ollama")));
    }

    [Fact]
    public void Ollama_IsCaseInsensitive()
    {
        Assert.IsType<FallbackAiModelClient>(Resolve(("Ai:Provider", "ollama")));
    }

    [Fact]
    public void Hybrid_ResolvesTheRulesOnTopOfTheLlm()
    {
        Assert.IsType<HybridAiModelClient>(Resolve(("Ai:Provider", "Hybrid")));
    }

    [Fact]
    public void Hybrid_AlsoKeepsTheFallbackUnderneath()
    {
        // No se puede inspeccionar la capa interna desde afuera, pero sí que apagar el fallback no
        // rompe el cableado: Hybrid sigue siendo Hybrid, sólo cambia qué envuelve.
        Assert.IsType<HybridAiModelClient>(Resolve(("Ai:Provider", "Hybrid"), ("Ai:FallbackToDeterministic", "false")));
    }

    [Fact]
    public void EveryExtractionPropertyIsRequired()
    {
        // Es la lección del benchmark: con propiedades opcionales el modelo las OMITE en vez de
        // responder null, y se pierden duración, viajeros y fechas. Ver docs/ai-evaluation.md.
        var schema = AiJsonSchemas.Extraction.AsObject();
        var properties = schema["properties"]!.AsObject().Select(p => p.Key).OrderBy(k => k);
        var required = schema["required"]!.AsArray().Select(n => n!.GetValue<string>()).OrderBy(k => k);

        Assert.Equal(properties, required);
    }

    [Fact]
    public void FallbackCanBeTurnedOff_ForMeasuringTheRawModel()
    {
        var client = Resolve(("Ai:Provider", "Ollama"), ("Ai:FallbackToDeterministic", "false"));

        Assert.IsType<OllamaAiModelClient>(client);
    }
}
