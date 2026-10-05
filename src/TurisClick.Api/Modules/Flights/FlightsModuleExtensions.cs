using TurisClick.Api.Modules.Flights.Services;
using TurisClick.Api.Modules.Flights.Services.Providers;
using TurisClick.Api.Modules.Flights.Services.Providers.Duffel;

namespace TurisClick.Api.Modules.Flights;

public static class FlightsModuleExtensions
{
    /// <summary>
    /// `Flights:Provider` elige el proveedor aéreo en el arranque — mismo patrón que `Ai:Provider`. El
    /// default es "Fake", que no toca la red: lo usan los tests, el entorno desplegado y cualquier demo
    /// sin credenciales. "Duffel" se activa sólo donde hay un token, que vive en User Secrets.
    ///
    /// El módulo todavía no expone controllers ni entidades: esta fase prueba la integración, no la
    /// vende. Lo que se registra acá es lo que consumirá el flujo de paquetes con vuelo.
    /// </summary>
    public static IServiceCollection AddFlightsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FlightsOptions>(configuration.GetSection(FlightsOptions.SectionName));

        var provider = configuration.GetSection(FlightsOptions.SectionName)["Provider"] ?? "Fake";

        if (string.Equals(provider, "Duffel", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<DuffelFlightProvider>();
            services.AddScoped<IFlightProvider>(sp => sp.GetRequiredService<DuffelFlightProvider>());
        }
        else
        {
            services.AddScoped<IFlightProvider>(_ => new FakeFlightProvider());
        }

        services.AddScoped<IPackageFlightService, PackageFlightService>();

        return services;
    }
}
