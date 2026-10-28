using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using TurisClick.Api.Modules.Flights.Services;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Flights.ProviderFlights;

/// <summary>
/// Vuelos de operador (Oleada 18) apagados por configuración desde la Oleada 19 (metabuscador). Se responde
/// **501 FEATURE_NOT_AVAILABLE**: RFC 9110 lo define como "el servidor no soporta la funcionalidad requerida",
/// que es exactamente esto, y es el contrato que el Backoffice y la app ya entienden (Oleada 18, B1). No es
/// 404 (el recurso existe y se puede volver a prender) ni 403 (no depende de quién lo pida).
/// </summary>
public static class ProviderFlightsFeature
{
    public const string DisabledMessage =
        "Los vuelos de TurisClick funcionan como buscador: la compra se hace en el sitio del vendedor. " +
        "La venta de vuelos dentro de TurisClick no está disponible.";

    public static void EnsureEnabled(FlightsOptions options)
    {
        if (!options.ProviderFlights.Enabled) throw new NotImplementedYetAppException(DisabledMessage);
    }
}

/// <summary>
/// En un controller o acción: 501 si los vuelos de operador están apagados. Es un filtro de recurso: corre
/// después de la autorización (sin sesión sigue siendo 401) y antes de leer y validar el cuerpo, así que un
/// pedido a una funcionalidad apagada no recibe un 400 que sugiera corregirlo.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireProviderFlightsAttribute : Attribute, IAsyncResourceFilter
{
    public Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var options = context.HttpContext.RequestServices.GetRequiredService<IOptionsSnapshot<FlightsOptions>>();
        ProviderFlightsFeature.EnsureEnabled(options.Value);
        return next();
    }
}
