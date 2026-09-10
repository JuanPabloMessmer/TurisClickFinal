namespace TurisClick.Api.Modules.Ai.Dtos;

/// <summary>
/// UC-T-18. El request NO lleva productos, precios, monedas ni availabilities: solo dice "quiero
/// reservar ESTE itinerario". Todo lo demás lo deriva el backend desde Postgres.
/// </summary>
public class BookItineraryRequest
{
    /// <summary>
    /// Mismo contrato que el pago (UC-SYS-02/`PayReservationRequest`): si algún precio o moneda cambió
    /// respecto de lo que el turista vio, el booking se detiene salvo que lo acepte explícitamente.
    /// </summary>
    public bool AcceptPriceChanges { get; set; }
}
