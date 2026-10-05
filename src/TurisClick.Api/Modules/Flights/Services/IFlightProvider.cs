namespace TurisClick.Api.Modules.Flights.Services;

/// <summary>
/// Inventario aéreo, en el lenguaje de TurisClick. Ninguna implementación filtra sus DTOs: lo que entra
/// y sale de acá son modelos propios, para que cambiar de proveedor —Duffel hoy; KIU, Amadeus
/// Enterprise, Sabre o Travelport mañana— no toque una línea del dominio.
///
/// Las tres operaciones son las que exige el ciclo real de una oferta aérea: buscar, volver a mirar
/// justo antes de comprar (el precio de una búsqueda no está garantizado) y reservar. `GetOrderAsync`
/// no es un extra: es lo que permite reconciliar una orden que el proveedor creó y nosotros no llegamos
/// a registrar.
/// </summary>
public interface IFlightProvider
{
    /// <summary>Nombre del proveedor tal como se persiste y se muestra en logs ("Duffel", "Fake").</summary>
    string Name { get; }

    Task<FlightSearchResult> SearchAsync(FlightSearchRequest request, CancellationToken ct);

    /// <summary>
    /// Vuelve a pedir la oferta al proveedor antes de reservar. Puede devolver otro precio, u otra
    /// disponibilidad: eso no es un error, es el caso normal que el flujo tiene que contemplar.
    /// </summary>
    Task<FlightOffer> RefreshOfferAsync(string offerId, CancellationToken ct);

    Task<FlightOrderResult> CreateOrderAsync(FlightOrderRequest request, CancellationToken ct);

    Task<FlightOrderResult?> GetOrderAsync(string orderId, CancellationToken ct);

    /// <summary>
    /// Busca una orden de la que NO tenemos el id. Es la operación que vuelve resoluble el peor caso del
    /// flujo: la llamada a <see cref="CreateOrderAsync"/> salió, la respuesta no llegó, y hay que
    /// averiguar si del otro lado quedó una compra antes de intentar otra.
    ///
    /// Se busca por la oferta con la que se pidió y por la clave de correlación que nosotros generamos
    /// antes de llamar; devolver null significa "el proveedor no tiene ninguna orden nuestra para esa
    /// oferta", nunca "no pude averiguarlo" —eso es una excepción.
    /// </summary>
    Task<FlightOrderResult?> FindOrderByOfferAsync(string offerId, string? correlationKey, CancellationToken ct);

    /// <summary>
    /// Cancela una orden. El resultado dice qué se devuelve y a dónde: TurisClick no asume que una
    /// cancelación implique reembolso, lo informa el proveedor.
    /// </summary>
    Task<FlightCancellationResult> CancelOrderAsync(string orderId, bool confirm, CancellationToken ct);
}
