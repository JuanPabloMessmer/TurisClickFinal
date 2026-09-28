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
    public void FallbackCanBeTurnedOff_ForMeasuringTheRawModel()
    {
        var client = Resolve(("Ai:Provider", "Ollama"), ("Ai:FallbackToDeterministic", "false"));

        Assert.IsType<OllamaAiModelClient>(client);
    }
}
