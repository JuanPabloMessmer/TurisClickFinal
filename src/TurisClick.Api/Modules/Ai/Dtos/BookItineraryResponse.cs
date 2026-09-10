using TurisClick.Api.Modules.Reservations.Dtos;

namespace TurisClick.Api.Modules.Ai.Dtos;

/// <summary>
/// UC-T-18. Si el precio o la moneda cambiaron y el turista no los aceptó, se devuelve 200 con
/// `RequiresPriceAcceptance = true`, `Reservation = null` y el detalle de los cambios — mismo criterio
/// que UC-T-19 en `ReservationResponse.RequiresPriceAcceptance` (no se inventa otra política): no se
/// toma ningún cupo, no se crea la reserva y el itinerario no queda BOOKED.
/// </summary>
public class BookItineraryResponse
{
    /// <summary>Presente solo cuando el booking se completó.</summary>
    public ReservationResponse? Reservation { get; set; }

    public bool RequiresPriceAcceptance { get; set; }

    /// <summary>Qué cambió respecto de lo que el turista vio en la propuesta.</summary>
    public List<ItineraryPriceChangeResponse> Changes { get; set; } = [];
}

public class ItineraryPriceChangeResponse
{
    public Guid ItineraryItemId { get; set; }
    public string ProductType { get; set; } = string.Empty;
    public string ProductTitle { get; set; } = string.Empty;

    /// <summary>`PRICE_CHANGED` o `CURRENCY_CHANGED` — un cambio de moneda no es un cambio numérico de precio y se reporta aparte.</summary>
    public string ChangeType { get; set; } = string.Empty;

    /// <summary>Lo que el turista vio en la propuesta (snapshot del AiItineraryItem, que no se modifica).</summary>
    public decimal PreviousUnitPrice { get; set; }
    public string PreviousCurrency { get; set; } = string.Empty;

    /// <summary>Lo que cobra hoy el proveedor.</summary>
    public decimal CurrentUnitPrice { get; set; }
    public string CurrentCurrency { get; set; } = string.Empty;
}
