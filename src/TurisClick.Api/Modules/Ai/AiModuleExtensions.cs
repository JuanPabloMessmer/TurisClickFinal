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

        var provider = configuration.GetSection(AiOptions.SectionName)["Provider"] ?? "Deterministic";
        if (string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IAiModelClient, OllamaAiModelClient>();
        }
        else
        {
            services.AddScoped<IAiModelClient, DeterministicAiModelClient>();
        }

        return services;
    }
}
