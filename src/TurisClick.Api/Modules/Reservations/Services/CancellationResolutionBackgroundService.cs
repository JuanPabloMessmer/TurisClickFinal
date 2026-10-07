using Microsoft.Extensions.Options;

namespace TurisClick.Api.Modules.Reservations.Services;

/// <summary>Configuración del proceso que resuelve cancelaciones a medias (sección `Reservations:CancellationResolution`).</summary>
public class CancellationResolutionOptions
{
    public const string SectionName = "Reservations:CancellationResolution";

    /// <summary>Los tests lo apagan: ahí la resolución se invoca a mano, sin esperar un timer.</summary>
    public bool Enabled { get; set; } = true;

    public int IntervalSeconds { get; set; } = 60;

    /// <summary>Cuántas cancelaciones sin resolver se procesan por pasada. Son pocas por definición.</summary>
    public int BatchSize { get; set; } = 20;
}

/// <summary>
/// Retoma las cancelaciones que quedaron a medias: un reembolso que la pasarela rechazó, o una cancelación
/// aérea cuyo desenlace no llegó a conocerse.
///
/// Igual de tonto que los otros dos procesos de fondo del sistema —despierta, delega y nada más—, por la
/// misma razón: así toda la lógica se prueba invocando el servicio, sin esperar relojes reales.
/// </summary>
public class CancellationResolutionBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<CancellationResolutionOptions> options,
    ILogger<CancellationResolutionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            logger.LogInformation("Resolución de cancelaciones desactivada por configuración.");
            return;
        }

        logger.LogInformation(
            "Resolución de cancelaciones activa: cada {Interval}s, lotes de {BatchSize}.",
            settings.IntervalSeconds, settings.BatchSize);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(settings.IntervalSeconds, 1)));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IReservationCancellationService>();
                await service.ResolvePendingAsync(settings.BatchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Nunca dejar morir el loop por un error puntual: la próxima pasada reintenta.
                logger.LogError(ex, "Falló la pasada de resolución de cancelaciones; se reintenta en la siguiente.");
            }
        }
    }
}
