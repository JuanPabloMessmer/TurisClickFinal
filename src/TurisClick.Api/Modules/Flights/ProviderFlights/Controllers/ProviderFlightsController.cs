using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Flights.ProviderFlights.Dtos;
using TurisClick.Api.Modules.Flights.ProviderFlights.Services;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Flights.ProviderFlights.Controllers;

/// <summary>
/// El operador publica y administra sus vuelos, salidas, asientos y tarifas, y registra la emisión de los
/// pasajes vendidos. El catálogo exige que la empresa esté autorizada a vender vuelos (403
/// FLIGHT_SELLER_NOT_AUTHORIZED); la emisión de lo ya vendido, no.
///
/// Contratos en docs/vuelos-sueltos-contratos.md.
/// </summary>
[ApiController]
[Route("api/companies/me")]
[Authorize(Policy = "RequireProvider")]
public class ProviderFlightsController(IProviderFlightService service) : ControllerBase
{
    [HttpGet("flights")]
    public async Task<ActionResult<List<ProviderFlightResponse>>> List(CancellationToken ct) =>
        Ok(await service.ListMineAsync(ct));

    /// <summary>Crea el vuelo en DRAFT.</summary>
    [HttpPost("flights")]
    [ProducesResponseType(typeof(ProviderFlightResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ProviderFlightResponse>> Create([FromBody] ProviderFlightRequest request, CancellationToken ct)
    {
        var created = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet("flights/{id:guid}")]
    public async Task<ActionResult<ProviderFlightResponse>> Get(Guid id, CancellationToken ct) =>
        Ok(await service.GetMineAsync(id, ct));

    /// <summary>Con reservas, la ruta, la aerolínea, el número de vuelo, la cabina y el equipaje no cambian (409).</summary>
    [HttpPut("flights/{id:guid}")]
    public async Task<ActionResult<ProviderFlightResponse>> Update(Guid id, [FromBody] ProviderFlightRequest request, CancellationToken ct) =>
        Ok(await service.UpdateAsync(id, request, ct));

    /// <summary>Publica el vuelo. Exige al menos una salida futura abierta con asientos.</summary>
    [HttpPost("flights/{id:guid}/publish")]
    public async Task<ActionResult<ProviderFlightResponse>> Publish(Guid id, CancellationToken ct) =>
        Ok(await service.PublishAsync(id, ct));

    [HttpPost("flights/{id:guid}/archive")]
    public async Task<ActionResult<ProviderFlightResponse>> Archive(Guid id, CancellationToken ct) =>
        Ok(await service.ArchiveAsync(id, ct));

    [HttpGet("flights/{id:guid}/departures")]
    public async Task<ActionResult<List<ProviderFlightDepartureResponse>>> ListDepartures(Guid id, CancellationToken ct) =>
        Ok(await service.ListDeparturesAsync(id, ct));

    [HttpPost("flights/{id:guid}/departures")]
    [ProducesResponseType(typeof(ProviderFlightDepartureResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ProviderFlightDepartureResponse>> CreateDeparture(
        Guid id, [FromBody] ProviderFlightDepartureRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await service.CreateDepartureAsync(id, request, ct));

    /// <summary>Con reservas, los asientos no bajan de los ya vendidos y los horarios y tarifas no cambian (409).</summary>
    [HttpPut("flight-departures/{departureId:guid}")]
    public async Task<ActionResult<ProviderFlightDepartureResponse>> UpdateDeparture(
        Guid departureId, [FromBody] ProviderFlightDepartureRequest request, CancellationToken ct) =>
        Ok(await service.UpdateDepartureAsync(departureId, request, ct));

    /// <summary>Sólo sin reservas; con reservas, se cierra (409).</summary>
    [HttpDelete("flight-departures/{departureId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteDeparture(Guid departureId, CancellationToken ct)
    {
        await service.DeleteDepartureAsync(departureId, ct);
        return NoContent();
    }

    /// <summary>Deja de vender la salida; las reservas hechas se mantienen.</summary>
    [HttpPost("flight-departures/{departureId:guid}/close")]
    public async Task<ActionResult<ProviderFlightDepartureResponse>> CloseDeparture(Guid departureId, CancellationToken ct) =>
        Ok(await service.CloseDepartureAsync(departureId, ct));

    /// <summary>
    /// Registra que el operador emitió los pasajes de un ítem pagado: un número por pasajero, infantes incluidos,
    /// en el orden de la reserva. Es lo único que lleva el ítem a ISSUED y lo único que hace que la app diga
    /// "Pasaje emitido".
    /// </summary>
    [HttpPost("reservation-items/{itemId:guid}/issuance")]
    public async Task<ActionResult<FlightItemIssuanceResponse>> RecordIssuance(
        Guid itemId, [FromBody] RecordIssuanceRequest request, CancellationToken ct) =>
        Ok(await service.RecordIssuanceAsync(itemId, request, ct));

    /// <summary>El operador no pudo emitir: cancela el ítem con reembolso total (simulado) y libera los asientos.</summary>
    [HttpPost("reservation-items/{itemId:guid}/issuance-failure")]
    public async Task<ActionResult<FlightItemIssuanceResponse>> RecordIssuanceFailure(
        Guid itemId, [FromBody] IssuanceFailureRequest request, CancellationToken ct) =>
        Ok(await service.RecordIssuanceFailureAsync(itemId, request, ct));
}

/// <summary>Moderación de vuelos de operador y autorización de empresas para venderlos.</summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = "RequireAdmin")]
public class AdminProviderFlightsController(IAdminProviderFlightService service) : ControllerBase
{
    [HttpPut("companies/{companyId:guid}/flight-seller")]
    public async Task<ActionResult<FlightSellerAuthorizationResponse>> SetFlightSeller(
        Guid companyId, [FromBody] FlightSellerAuthorizationRequest request, CancellationToken ct) =>
        Ok(await service.SetFlightSellerAsync(companyId, request, ct));

    [HttpGet("flights")]
    public async Task<ActionResult<PagedResult<ProviderFlightResponse>>> List(
        [FromQuery] AdminProviderFlightsQuery query, CancellationToken ct) =>
        Ok(await service.ListAsync(query, ct));

    /// <summary>Un vuelo suspendido sale de la búsqueda; las reservas ya hechas siguen su curso.</summary>
    [HttpPost("flights/{id:guid}/suspend")]
    public async Task<ActionResult<ProviderFlightResponse>> Suspend(Guid id, CancellationToken ct) =>
        Ok(await service.SetSuspendedAsync(id, suspended: true, ct));

    [HttpPost("flights/{id:guid}/unsuspend")]
    public async Task<ActionResult<ProviderFlightResponse>> Unsuspend(Guid id, CancellationToken ct) =>
        Ok(await service.SetSuspendedAsync(id, suspended: false, ct));
}
