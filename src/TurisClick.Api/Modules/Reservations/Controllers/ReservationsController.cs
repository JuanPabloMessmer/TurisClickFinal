using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Reservations.Dtos;
using TurisClick.Api.Modules.Reservations.Services;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Reservations.Controllers;

[ApiController]
[Route("api/reservations")]
[Authorize(Policy = "RequireTourist")]
public class ReservationsController(IReservationService reservationService) : ControllerBase
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
}
