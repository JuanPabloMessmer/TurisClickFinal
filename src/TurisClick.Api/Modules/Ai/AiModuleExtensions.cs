using TurisClick.Api.Modules.Ai.Repositories;
using TurisClick.Api.Modules.Ai.Services;
using TurisClick.Api.Modules.Ai.Services.LlmClients;

namespace TurisClick.Api.Modules.Ai;

public static class AiModuleExtensions
{
    /// <summary>
    /// Ai:Provider selecciona la implementación de IAiModelClient en tiempo de arranque — nunca
    /// acoplado a un Service concreto (docs de la sesión, sección 5). "Deterministic" (default) no
    /// depende de ningún proceso externo — es lo que usan los tests y Newman. "Ollama" es el proveedor
    /// de desarrollo principal del proyecto, corre localmente vía HTTP contra Ai:Ollama:BaseUrl.
    /// "Hybrid" es Ollama con las reglas ganando en los escalares literales (duración, viajeros, fechas,
    /// presupuesto); es el que mejor puntúa en el benchmark — ver docs/ai-evaluation.md.
    /// </summary>
    public static IServiceCollection AddAiModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));

        services.AddScoped<IAiConversationRepository, AiConversationRepository>();
        services.AddScoped<IAiItineraryRepository, AiItineraryRepository>();
        services.AddScoped<IAiCatalogRepository, AiCatalogRepository>();
        services.AddScoped<IRetrievalService, RetrievalService>();
        services.AddScoped<IItineraryRevalidationService, ItineraryRevalidationService>();
        services.AddScoped<IAiConversationService, AiConversationService>();
        services.AddScoped<IAiItineraryService, AiItineraryService>();
        services.AddScoped<IAiItineraryBookingService, AiItineraryBookingService>();

        // El cliente determinístico SIEMPRE está registrado: con Ai:Provider=Deterministic es el agente
        // completo, y con un proveedor LLM es la red de seguridad (FallbackAiModelClient).
        services.AddScoped<DeterministicAiModelClient>();

        var section = configuration.GetSection(AiOptions.SectionName);
        var provider = section["Provider"] ?? "Deterministic";
        var fallbackEnabled = !bool.TryParse(section["FallbackToDeterministic"], out var configured) || configured;

        var usesLlm = string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "Hybrid", StringComparison.OrdinalIgnoreCase);

        if (usesLlm)
        {
            // HttpClient tipado para el adapter de Ollama; el timeout fino lo maneja el propio cliente.
            services.AddHttpClient<OllamaAiModelClient>();

            services.AddScoped<IAiModelClient>(sp =>
            {
                IAiModelClient client = sp.GetRequiredService<OllamaAiModelClient>();

                if (fallbackEnabled)
                {
                    client = new FallbackAiModelClient(
                        client,
                        sp.GetRequiredService<DeterministicAiModelClient>(),
                        sp.GetRequiredService<ILogger<FallbackAiModelClient>>());
                }

                // Hybrid agrega una capa más: las reglas ganan en los escalares que están escritos
                // literalmente en el mensaje (ver HybridAiModelClient y docs/ai-evaluation.md).
                if (string.Equals(provider, "Hybrid", StringComparison.OrdinalIgnoreCase))
                {
                    client = new HybridAiModelClient(
                        client,
                        sp.GetRequiredService<DeterministicAiModelClient>(),
                        sp.GetRequiredService<ILogger<HybridAiModelClient>>());
                }

                return client;
            });
        }
        else
        {
            services.AddScoped<IAiModelClient>(sp => sp.GetRequiredService<DeterministicAiModelClient>());
        }

        return services;
    }
}
