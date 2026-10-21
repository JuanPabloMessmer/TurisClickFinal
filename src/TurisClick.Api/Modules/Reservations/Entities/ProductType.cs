namespace TurisClick.Api.Modules.Reservations.Entities;

/// <summary>Compartido conceptualmente por ReservationItem y (más adelante) AiItineraryItem.</summary>
public enum ProductType
{
    EXPERIENCE,
    PACKAGE,

    /// <summary>Vuelo suelto publicado por un operador autorizado (Oleada 18). Sólo en ReservationItem.</summary>
    FLIGHT
}
