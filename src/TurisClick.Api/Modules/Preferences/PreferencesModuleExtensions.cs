using TurisClick.Api.Modules.Preferences.Services;

namespace TurisClick.Api.Modules.Preferences;

public static class PreferencesModuleExtensions
{
    public static IServiceCollection AddPreferencesModule(this IServiceCollection services)
    {
        services.AddScoped<ITouristPreferenceService, TouristPreferenceService>();
        return services;
    }
}
