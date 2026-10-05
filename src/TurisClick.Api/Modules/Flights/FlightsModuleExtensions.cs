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
    /// Además del proveedor, acá se registran la orquestación de reserva y el reconciliador: el proceso de
    /// fondo que resuelve las emisiones de desenlace desconocido se puede apagar por configuración, igual
    /// que el de expiración, porque los tests invocan la reconciliación a mano en vez de esperar un timer.
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
        services.AddScoped<IFlightBookingOrchestrator, FlightBookingOrchestrator>();
        services.AddScoped<IFlightReconciliationService, FlightReconciliationService>();

        services.Configure<FlightReconciliationOptions>(
            configuration.GetSection(FlightReconciliationOptions.SectionName));
        services.AddHostedService<FlightReconciliationBackgroundService>();

        return services;
    }
}
