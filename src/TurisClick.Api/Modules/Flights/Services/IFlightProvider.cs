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
}
