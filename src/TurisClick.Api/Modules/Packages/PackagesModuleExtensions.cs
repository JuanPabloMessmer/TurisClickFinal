using TurisClick.Api.Modules.Packages.Repositories;
using TurisClick.Api.Modules.Packages.Services;

namespace TurisClick.Api.Modules.Packages;

public static class PackagesModuleExtensions
{
    public static IServiceCollection AddPackagesModule(this IServiceCollection services)
    {
        services.AddScoped<IPackageRepository, PackageRepository>();
        services.AddScoped<IPackageService, PackageService>();
        services.AddScoped<IPackageAvailabilityRepository, PackageAvailabilityRepository>();
        services.AddScoped<IPackageAvailabilityService, PackageAvailabilityService>();
        return services;
    }
}
