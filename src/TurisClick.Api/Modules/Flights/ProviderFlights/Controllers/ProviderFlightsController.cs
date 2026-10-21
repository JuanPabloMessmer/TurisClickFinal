using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Flights.ProviderFlights.Dtos;
using TurisClick.Api.Shared.Exceptions;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Flights.ProviderFlights.Controllers;

/// <summary>
/// Firmas definitivas de los vuelos de operador (Workstream B1, contratos en docs/vuelos-sueltos-contratos.md).
///
/// Las rutas, los permisos, los tipos y la validación de entrada son los finales: el Backoffice y la app ya
/// trabajan contra ellos. La lógica llega con B2 (migración 0015); hasta entonces cada endpoint valida el
/// pedido y responde 501 con un mensaje que se puede mostrar. Ninguno escribe nada.
/// </summary>
internal static class ProviderFlightsPending
{
    public const string Message = "La venta de vuelos de operador todavía no está disponible.";

    public static NotImplementedYetAppException Exception() => new(Message);
}

/// <summary>
/// El operador publica y administra sus vuelos, salidas, asientos y tarifas, y registra la emisión de los
/// pasajes vendidos. Exige además que la empresa esté autorizada a vender vuelos (lo decide un ADMIN).
/// </summary>
[ApiController]
[Route("api/companies/me")]
[Authorize(Policy = "RequireProvider")]
public class ProviderFlightsController : ControllerBase
{
    [HttpGet("flights")]
    public ActionResult<List<ProviderFlightResponse>> List() => throw ProviderFlightsPending.Exception();

    /// <summary>Crea el vuelo en DRAFT.</summary>
    [HttpPost("flights")]
    [ProducesResponseType(typeof(ProviderFlightResponse), StatusCodes.Status201Created)]
    public ActionResult<ProviderFlightResponse> Create([FromBody] ProviderFlightRequest request) =>
        throw ProviderFlightsPending.Exception();

    [HttpGet("flights/{id:guid}")]
    public ActionResult<ProviderFlightResponse> Get(Guid id) => throw ProviderFlightsPending.Exception();

    [HttpPut("flights/{id:guid}")]
    public ActionResult<ProviderFlightResponse> Update(Guid id, [FromBody] ProviderFlightRequest request) =>
        throw ProviderFlightsPending.Exception();

    /// <summary>Publica el vuelo. Exige al menos una salida futura abierta con asientos.</summary>
    [HttpPost("flights/{id:guid}/publish")]
    public ActionResult<ProviderFlightResponse> Publish(Guid id) => throw ProviderFlightsPending.Exception();

    [HttpPost("flights/{id:guid}/archive")]
    public ActionResult<ProviderFlightResponse> Archive(Guid id) => throw ProviderFlightsPending.Exception();

    [HttpGet("flights/{id:guid}/departures")]
    public ActionResult<List<ProviderFlightDepartureResponse>> ListDepartures(Guid id) =>
        throw ProviderFlightsPending.Exception();

    [HttpPost("flights/{id:guid}/departures")]
    [ProducesResponseType(typeof(ProviderFlightDepartureResponse), StatusCodes.Status201Created)]
    public ActionResult<ProviderFlightDepartureResponse> CreateDeparture(Guid id, [FromBody] ProviderFlightDepartureRequest request) =>
        throw ProviderFlightsPending.Exception();

    /// <summary>Con reservas, los asientos no pueden bajar de los ya vendidos ni pueden cambiar las tarifas vendidas.</summary>
    [HttpPut("flight-departures/{departureId:guid}")]
    public ActionResult<ProviderFlightDepartureResponse> UpdateDeparture(Guid departureId, [FromBody] ProviderFlightDepartureRequest request) =>
        throw ProviderFlightsPending.Exception();

    /// <summary>Sólo sin reservas; con reservas, se cierra.</summary>
    [HttpDelete("flight-departures/{departureId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult DeleteDeparture(Guid departureId) => throw ProviderFlightsPending.Exception();

    /// <summary>Deja de vender la salida; las reservas hechas se mantienen.</summary>
    [HttpPost("flight-departures/{departureId:guid}/close")]
    public ActionResult<ProviderFlightDepartureResponse> CloseDeparture(Guid departureId) =>
        throw ProviderFlightsPending.Exception();

    /// <summary>
    /// Registra que el operador emitió los pasajes de un ítem pagado. Es lo único que lleva el ítem a ISSUED y
    /// lo único que hace que la app diga "Pasaje emitido".
    /// </summary>
    [HttpPost("reservation-items/{itemId:guid}/issuance")]
    public ActionResult<FlightItemIssuanceResponse> RecordIssuance(Guid itemId, [FromBody] RecordIssuanceRequest request) =>
        throw ProviderFlightsPending.Exception();

    /// <summary>El operador no pudo emitir: cancela el ítem con reembolso total (simulado) y libera los asientos.</summary>
    [HttpPost("reservation-items/{itemId:guid}/issuance-failure")]
    public ActionResult<FlightItemIssuanceResponse> RecordIssuanceFailure(Guid itemId, [FromBody] IssuanceFailureRequest request) =>
        throw ProviderFlightsPending.Exception();
}

/// <summary>Moderación de vuelos de operador y autorización de empresas para venderlos.</summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = "RequireAdmin")]
public class AdminProviderFlightsController : ControllerBase
{
    [HttpPut("companies/{companyId:guid}/flight-seller")]
    public ActionResult<FlightSellerAuthorizationResponse> SetFlightSeller(
        Guid companyId, [FromBody] FlightSellerAuthorizationRequest request) =>
        throw ProviderFlightsPending.Exception();

    [HttpGet("flights")]
    public ActionResult<PagedResult<ProviderFlightResponse>> List([FromQuery] AdminProviderFlightsQuery query) =>
        throw ProviderFlightsPending.Exception();

    /// <summary>Un vuelo suspendido sale de la búsqueda; las reservas ya hechas siguen su curso.</summary>
    [HttpPost("flights/{id:guid}/suspend")]
    public ActionResult<ProviderFlightResponse> Suspend(Guid id) => throw ProviderFlightsPending.Exception();

    [HttpPost("flights/{id:guid}/unsuspend")]
    public ActionResult<ProviderFlightResponse> Unsuspend(Guid id) => throw ProviderFlightsPending.Exception();
}
