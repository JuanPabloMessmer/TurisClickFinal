using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Reservations.Dtos;
using TurisClick.Api.Modules.Reservations.Services;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Reservations.Controllers;

[ApiController]
[Route("api/reservations")]
[Authorize(Policy = "RequireTourist")]
public class ReservationsController(
    IReservationService reservationService,
    IReservationCancellationService cancellationService) : ControllerBase
{
    /// <summary>UC-T-08 — reserva directa de una Experience individual.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ReservationResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ReservationResponse>> Create([FromBody] CreateReservationRequest request, CancellationToken ct)
    {
        var result = await reservationService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>UC-T-10 — "Mis reservas".</summary>
    [HttpGet("me")]
    public async Task<ActionResult<PagedResult<ReservationResponse>>> ListMine(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await reservationService.ListMineAsync(page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>UC-T-10 — detalle de una reserva propia (403 si no es dueño).</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReservationResponse>> GetById(Guid id, CancellationToken ct)
    {
        var result = await reservationService.GetByIdForTouristAsync(id, ct);
        return Ok(result);
    }

    /// <summary>UC-T-19 — pagar una reserva propia en PENDING_PAYMENT.</summary>
    [HttpPost("{id:guid}/pay")]
    public async Task<ActionResult<ReservationResponse>> Pay(Guid id, [FromBody] PayReservationRequest request, CancellationToken ct)
    {
        var result = await reservationService.PayAsync(id, request, ct);
        return Ok(result);
    }

    /// <summary>
    /// UC-T-22 — qué pasaría si cancelara: cuánto devuelve el operador por cada producto según la política
    /// que la reserva congeló, y cuánto devuelve la aerolínea según lo que ella misma informa. **No cancela
    /// nada.**
    ///
    /// El presupuesto se guarda y vence: confirmar significa aceptar exactamente estos importes, y el cliente
    /// nunca manda un monto de reembolso.
    /// </summary>
    [HttpPost("{id:guid}/cancellation-quote")]
    [ProducesResponseType(typeof(CancellationQuoteResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CancellationQuoteResponse>> QuoteCancellation(Guid id, CancellationToken ct)
        => Ok(await cancellationService.QuoteAsync(id, ct));

    /// <summary>
    /// UC-T-11 / UC-T-22 — cancelar la reserva y liberar el cupo.
    ///
    /// Sin pagar todavía se cancela directo. Ya confirmada hace falta el id del presupuesto aceptado, y el
    /// resultado puede ser parcial: si la aerolínea no confirma o el reembolso falla, se devuelve el estado
    /// real en vez de anunciar un éxito que no ocurrió.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<ReservationResponse>> Cancel(
        Guid id, [FromBody] ConfirmCancellationRequest? request, CancellationToken ct)
    {
        var result = await reservationService.CancelAsync(id, request?.CancellationQuoteId, ct);
        return Ok(result);
    }
}
