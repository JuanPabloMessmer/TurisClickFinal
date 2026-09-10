using Microsoft.Extensions.Options;

namespace TurisClick.Api.Modules.Reservations.Services;

/// <summary>
/// UC-SYS-08 — el "proceso propio de expiración por tiempo" que menciona el caso de uso. A propósito es
/// lo más delgado posible: no tiene ninguna regla de negocio, solo despierta cada N segundos y delega en
/// <see cref="IReservationExpirationService"/>. Así toda la lógica se puede testear sin esperar timers.
///
/// Con varias instancias del backend corriendo no hace falta coordinación externa: la transición
/// condicional de estado hace que, si dos instancias agarran la misma reserva, solo una libere el cupo.
/// </summary>
public class ReservationExpirationBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<ReservationExpirationOptions> options,
    ILogger<ReservationExpirationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            logger.LogInformation("Expiración automática de reservas desactivada por configuración.");
            return;
        }

        logger.LogInformation(
            "Expiración automática de reservas activa: cada {Interval}s, lotes de {BatchSize}.",
            settings.IntervalSeconds, settings.BatchSize);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(settings.IntervalSeconds, 1)));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                // Scope propio por pasada: el servicio y el DbContext son Scoped y este host es Singleton.
                using var scope = scopeFactory.CreateScope();
                var expirationService = scope.ServiceProvider.GetRequiredService<IReservationExpirationService>();
                await expirationService.ExpireDueReservationsAsync(settings.BatchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Nunca dejar morir el loop por un error puntual: la próxima pasada reintenta.
                logger.LogError(ex, "Fallo la pasada de expiración de reservas; se reintenta en la siguiente.");
            }
        }
    }
}
