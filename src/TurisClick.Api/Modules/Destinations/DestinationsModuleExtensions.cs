using TurisClick.Api.Modules.Destinations.Repositories;
using TurisClick.Api.Modules.Destinations.Services;

namespace TurisClick.Api.Modules.Destinations;

public static class DestinationsModuleExtensions
{
    public static IServiceCollection AddDestinationsModule(this IServiceCollection services)
    {
        services.AddScoped<IDestinationRepository, DestinationRepository>();
        services.AddScoped<IDestinationService, DestinationService>();
        return services;
    }
}
