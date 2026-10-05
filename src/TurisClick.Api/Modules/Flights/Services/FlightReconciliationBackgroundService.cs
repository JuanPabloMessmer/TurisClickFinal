using Microsoft.Extensions.Options;

namespace TurisClick.Api.Modules.Flights.Services;

/// <summary>
/// Despierta cada N segundos y delega: igual de delgado que el proceso de expiración de reservas, y por la
/// misma razón —toda la lógica tiene que poder probarse sin esperar un timer.
///
/// Con varias instancias del backend no hace falta coordinación externa: cada reconciliación gana o pierde
/// una transición condicional, así que dos instancias sobre el mismo vuelo no lo resuelven dos veces.
/// </summary>
public class FlightReconciliationBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<FlightReconciliationOptions> options,
    ILogger<FlightReconciliationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            logger.LogInformation("Reconciliación de vuelos desactivada por configuración.");
            return;
        }

        logger.LogInformation(
            "Reconciliación de vuelos activa: cada {Interval}s, lotes de {BatchSize}, hasta {MaxAttempts} consulta(s) por caso.",
            settings.IntervalSeconds, settings.BatchSize, settings.MaxAttempts);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(settings.IntervalSeconds, 1)));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IFlightReconciliationService>();
                await service.ReconcilePendingAsync(settings.BatchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Nunca dejar morir el loop por un error puntual: la próxima pasada reintenta.
                logger.LogError(ex, "Falló la pasada de reconciliación de vuelos; se reintenta en la siguiente.");
            }
        }
    }
}
