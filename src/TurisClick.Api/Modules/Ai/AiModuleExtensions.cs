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

        if (string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase))
        {
            // HttpClient tipado para el adapter de Ollama; el timeout fino lo maneja el propio cliente.
            services.AddHttpClient<OllamaAiModelClient>();

            if (fallbackEnabled)
            {
                services.AddScoped<IAiModelClient>(sp => new FallbackAiModelClient(
                    sp.GetRequiredService<OllamaAiModelClient>(),
                    sp.GetRequiredService<DeterministicAiModelClient>(),
                    sp.GetRequiredService<ILogger<FallbackAiModelClient>>()));
            }
            else
            {
                services.AddScoped<IAiModelClient>(sp => sp.GetRequiredService<OllamaAiModelClient>());
            }
        }
        else
        {
            services.AddScoped<IAiModelClient>(sp => sp.GetRequiredService<DeterministicAiModelClient>());
        }

        return services;
    }
}
