using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Reservations.Dtos;
using TurisClick.Api.Modules.Reservations.Services;

namespace TurisClick.Api.Modules.Reservations.Controllers;

/// <summary>
/// UC-A-10 — visibilidad operativa del dinero y de las cancelaciones. Es una vista de operación, no un
/// panel de finanzas: responde "qué pasó con la plata de esta reserva" y "qué quedó a medias", que es lo que
/// hace falta para resolver un caso, y nada más.
///
/// No expone datos de medio de pago porque no existen: el libro nunca los guarda.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = "RequireAdmin")]
public class AdminPaymentsController(
    IAdminPaymentsService service,
    IReservationCancellationService cancellationService) : ControllerBase
{
    /// <summary>El libro de una reserva, su saldo por moneda y sus cancelaciones.</summary>
    [HttpGet("reservations/{id:guid}/payments")]
    [ProducesResponseType(typeof(ReservationPaymentsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReservationPaymentsResponse>> GetPayments(Guid id, CancellationToken ct)
        => Ok(await service.GetReservationPaymentsAsync(id, ct));

    /// <summary>
    /// Las cancelaciones que no terminaron bien: reembolsos pendientes y casos en revisión. Es la cola de
    /// trabajo del ADMIN, y está primero lo más viejo porque es lo que más tiempo lleva esperando.
    /// </summary>
    [HttpGet("cancellations")]
    [ProducesResponseType(typeof(List<CancellationSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CancellationSummaryResponse>>> ListUnresolved(
        [FromQuery] int limit = 50, CancellationToken ct = default)
        => Ok(await service.ListUnresolvedCancellationsAsync(limit, ct));

    /// <summary>
    /// Reintenta ahora las cancelaciones que quedaron a medias, sin esperar al proceso de fondo.
    ///
    /// Existe porque el proceso de fondo vive dentro de la API, y en un App Service del plan gratuito la
    /// aplicación se duerme sin tráfico: mientras duerme, no reintenta nada. Hasta ahora el ADMIN podía VER
    /// la cola de pendientes pero no hacer nada con ella, y durante una demostración eso es una reserva
    /// cancelada cuyo reembolso se queda colgado sin salida.
    ///
    /// No hace nada nuevo: llama a la misma resolución que corre sola, que ya es idempotente —un reembolso
    /// exitoso no se repite— así que invocarla dos veces no cobra ni devuelve dos veces.
    /// </summary>
    [HttpPost("cancellations/resolve")]
    [ProducesResponseType(typeof(ResolvePendingCancellationsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ResolvePendingCancellationsResponse>> ResolvePending(
        [FromQuery] int batchSize = 20, CancellationToken ct = default)
    {
        var pendientesAntes = (await service.ListUnresolvedCancellationsAsync(batchSize, ct)).Count;
        var resueltas = await cancellationService.ResolvePendingAsync(Math.Clamp(batchSize, 1, 100), ct);
        var pendientesDespues = (await service.ListUnresolvedCancellationsAsync(batchSize, ct)).Count;

        return Ok(new ResolvePendingCancellationsResponse
        {
            Attempted = pendientesAntes,
            Completed = resueltas,
            StillPending = pendientesDespues,
        });
    }
}
