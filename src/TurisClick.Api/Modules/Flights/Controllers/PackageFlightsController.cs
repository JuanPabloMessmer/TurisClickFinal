using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
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
/// Cotización de vuelo de un paquete. **Exige sesión de turista**, y eso cambió a propósito respecto de la
/// oleada anterior: cotizar no es leer el catálogo, es pedirle inventario a un proveedor externo y escribir
/// filas en nuestra base. Dejarlo abierto convertía cada visita anónima en consultas al proveedor y en
/// cotizaciones que nadie iba a usar.
///
/// El catálogo sigue siendo público: la ficha de un paquete con vuelo se ve sin cuenta, dice desde qué
/// ciudades sale y a dónde llega, y pide iniciar sesión para ver precios de pasaje. Esa es la frontera más
/// simple que resuelve el abuso sin inventar una plataforma de rate limiting.
///
/// El cliente nunca manda precios ni ids del proveedor: manda desde dónde sale, para qué salida y
/// cuántos son. Todo lo demás lo decide el servidor.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireTourist")]
public class PackageFlightQuotesController(IPackageFlightService service) : ControllerBase
{
    [HttpPost("api/packages/{packageId:guid}/flight-quotes")]
    public async Task<ActionResult<PackageFlightQuoteResponse>> Quote(
        Guid packageId, [FromBody] PackageFlightQuoteRequest request, CancellationToken ct)
        => Ok(await service.QuoteAsync(packageId, request, ct));

    /// <summary>
    /// Vuelve a preguntarle al proveedor por una cotización ya mostrada. Devuelve si sigue igual, si
    /// cambió de precio, si venció o si ya no existe — y en el caso del cambio, los dos importes, para
    /// que la persona acepte explícitamente antes de seguir.
    /// </summary>
    [HttpPost("api/flight-quotes/{quoteId:guid}/revalidate")]
    public async Task<ActionResult<FlightQuoteRevalidationResponse>> Revalidate(Guid quoteId, CancellationToken ct)
        => Ok(await service.RevalidateAsync(quoteId, ct));
}

/// <summary>
/// Búsqueda de vuelos sin paquete, para el buscador de la app.
///
/// **Sin sesión mientras el proveedor sea el falso** (`Flights:AllowAnonymousSearch`, ver FlightsOptions):
/// buscar y comparar no debería exigir una cuenta, y con el proveedor simulado no hay inventario externo que
/// proteger. Con un proveedor real vuelve a exigir sesión, salvo decisión explícita, porque cada búsqueda
/// anónima sería una consulta a su API. La sesión se exige siempre al reservar: eso vive en /api/reservations.
///
/// No escribe nada y no devuelve ningún identificador que alcance para cobrar: lo reservable se reserva en
/// /api/reservations, con sesión.
/// </summary>
[ApiController]
[Route("api/flights")]
[AllowAnonymous]
public class FlightSearchController(IFlightSearchService service, IOptions<FlightsOptions> options) : ControllerBase
{
    [HttpPost("search")]
    [ProducesResponseType(typeof(SearchFlightsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SearchFlightsResponse>> Search(
        [FromBody] SearchFlightsRequest request, CancellationToken ct)
    {
        if (!options.Value.AnonymousSearchAllowed && User.Identity?.IsAuthenticated != true)
            return Challenge();

        return Ok(await service.SearchAsync(request, ct));
    }
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

    /// <summary>
    /// Autocompletado de aeropuertos del mundo (OurAirports), por ciudad, nombre, país o código IATA.
    ///
    /// `role=destination` limita a Bolivia, que es a donde se puede volar por ahora; `role=origin` (default)
    /// busca en todo el mundo. Sin `q` devuelve sugerencias. Es público: el dataset vive en memoria y no
    /// consulta a ningún proveedor, así que no hay inventario que proteger.
    /// </summary>
    [HttpGet("search")]
    [AllowAnonymous]
    public ActionResult<IEnumerable<AirportSearchResultResponse>> Search(
        [FromQuery] string? q, [FromQuery] string? role, [FromQuery] int limit = 10)
    {
        var searchRole = string.Equals(role, "destination", StringComparison.OrdinalIgnoreCase)
            ? AirportCatalog.SearchRole.Destination
            : AirportCatalog.SearchRole.Origin;

        // Un texto larguísimo no encuentra nada que uno corto no encuentre: se recorta en vez de rechazar.
        var query = q is { Length: > 60 } ? q[..60] : q;

        return Ok(AirportCatalog.Search(query, searchRole, Math.Clamp(limit, 1, 25))
            .Select(a => new AirportSearchResultResponse
            {
                Iata = a.Iata,
                Name = a.Name,
                City = a.City,
                Country = a.Country,
                CountryCode = a.CountryCode,
                Label = $"{a.City} ({a.Iata})",
            }));
    }
}
