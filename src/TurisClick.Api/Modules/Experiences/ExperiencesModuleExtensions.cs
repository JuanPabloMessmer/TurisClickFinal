using TurisClick.Api.Modules.Experiences.Repositories;
using TurisClick.Api.Modules.Experiences.Services;

namespace TurisClick.Api.Modules.Experiences;

public static class ExperiencesModuleExtensions
{
    public static IServiceCollection AddExperiencesModule(this IServiceCollection services)
    {
        services.AddScoped<IExperienceRepository, ExperienceRepository>();
        services.AddScoped<IExperienceService, ExperienceService>();
        services.AddScoped<IExperienceAvailabilityRepository, ExperienceAvailabilityRepository>();
        services.AddScoped<IExperienceAvailabilityService, ExperienceAvailabilityService>();
        return services;
    }
}
