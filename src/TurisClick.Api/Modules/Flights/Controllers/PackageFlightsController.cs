using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Flights.Dtos;
using TurisClick.Api.Modules.Flights.Services;

namespace TurisClick.Api.Modules.Flights.Controllers;

/// <summary>
/// Reglas de vuelo de un paquete. El operador las configura para los suyos; el ADMIN las puede leer
/// para cualquiera, porque la moderación global necesita ver qué se está vendiendo.
/// </summary>
[ApiController]
[Route("api/packages/{packageId:guid}/flight-rule")]
public class PackageFlightRulesController(IPackageFlightService service) : ControllerBase
{
    [HttpPut]
    [Authorize(Policy = "RequireProvider")]
    public async Task<ActionResult<PackageFlightRuleResponse>> Set(
        Guid packageId, [FromBody] PackageFlightRuleRequest request, CancellationToken ct)
        => Ok(await service.SetRuleAsync(packageId, request, ct));

    [HttpDelete]
    [Authorize(Policy = "RequireProvider")]
    public async Task<IActionResult> Remove(Guid packageId, CancellationToken ct)
    {
        await service.RemoveRuleAsync(packageId, ct);
        return NoContent();
    }

    /// <summary>Lectura para el dueño del paquete o para un ADMIN (visibilidad global de la plataforma).</summary>
    [HttpGet]
    [Authorize(Policy = "RequireProviderOrAdmin")]
    public async Task<ActionResult<PackageFlightRuleResponse>> Get(Guid packageId, CancellationToken ct)
    {
        var rule = await service.GetRuleAsync(packageId, ct);
        return rule is null ? NoContent() : Ok(rule);
    }
}

/// <summary>
/// Cotización de vuelo de un paquete. Es pública, igual que el resto del catálogo: la persona puede ver
/// precios reales antes de crear una cuenta, y la sesión se pide al reservar.
///
/// El cliente nunca manda precios ni ids del proveedor: manda desde dónde sale, para qué salida y
/// cuántos son. Todo lo demás lo decide el servidor.
/// </summary>
[ApiController]
public class PackageFlightQuotesController(IPackageFlightService service) : ControllerBase
{
    [HttpPost("api/packages/{packageId:guid}/flight-quotes")]
    [AllowAnonymous]
    public async Task<ActionResult<PackageFlightQuoteResponse>> Quote(
        Guid packageId, [FromBody] PackageFlightQuoteRequest request, CancellationToken ct)
        => Ok(await service.QuoteAsync(packageId, request, ct));

    /// <summary>
    /// Vuelve a preguntarle al proveedor por una cotización ya mostrada. Devuelve si sigue igual, si
    /// cambió de precio, si venció o si ya no existe — y en el caso del cambio, los dos importes, para
    /// que la persona acepte explícitamente antes de seguir.
    /// </summary>
    [HttpPost("api/flight-quotes/{quoteId:guid}/revalidate")]
    [AllowAnonymous]
    public async Task<ActionResult<FlightQuoteRevalidationResponse>> Revalidate(Guid quoteId, CancellationToken ct)
        => Ok(await service.RevalidateAsync(quoteId, ct));
}

/// <summary>Catálogo de aeropuertos conocidos, para que el Backoffice no tenga que hardcodear la lista.</summary>
[ApiController]
[Route("api/airports")]
public class AirportsController : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public ActionResult<IEnumerable<AirportResponse>> List([FromQuery] string? country)
    {
        var airports = string.Equals(country, "BO", StringComparison.OrdinalIgnoreCase)
            ? AirportCatalog.Bolivian
            : AirportCatalog.All;

        return Ok(airports
            .OrderBy(a => a.Country == "Bolivia" ? 0 : 1)
            .ThenBy(a => a.City, StringComparer.CurrentCulture)
            .Select(a => new AirportResponse { Iata = a.Iata, Label = $"{a.City} ({a.Iata})" }));
    }
}
