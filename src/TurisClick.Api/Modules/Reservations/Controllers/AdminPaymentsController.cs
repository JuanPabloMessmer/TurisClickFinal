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
public class AdminPaymentsController(IAdminPaymentsService service) : ControllerBase
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
}
