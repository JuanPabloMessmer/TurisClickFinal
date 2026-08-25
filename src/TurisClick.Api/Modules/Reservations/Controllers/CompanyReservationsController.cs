using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Reservations.Dtos;
using TurisClick.Api.Modules.Reservations.Services;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Reservations.Controllers;

/// <summary>UC-P-12/13 — reservas recibidas por la empresa del PROVIDER autenticado, a nivel de ReservationItem.</summary>
[ApiController]
[Route("api/companies/me/reservations")]
[Authorize(Policy = "RequireProvider")]
public class CompanyReservationsController(IReservationService reservationService) : ControllerBase
{
    /// <summary>UC-P-12.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ReservationItemResponse>>> ListReceived(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await reservationService.ListReceivedByCompanyAsync(page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>UC-P-13 — 403 si el ReservationItem no pertenece a la empresa del Provider autenticado.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReservationItemResponse>> GetById(Guid id, CancellationToken ct)
    {
        var result = await reservationService.GetReceivedItemByIdAsync(id, ct);
        return Ok(result);
    }
}
