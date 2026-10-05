using TurisClick.Api.Modules.Flights.Entities;

namespace TurisClick.Api.Modules.Flights.Services;

/// <summary>
/// Traduce "este paquete sale el 19 de noviembre y dura 7 días" a las fechas concretas del vuelo.
///
/// Que esto sea una regla y no un campo es el punto entero del diseño: el operador configura una vez la
/// relación entre el viaje y los vuelos, y cada salida del paquete deriva sus fechas sola. Si cargara
/// fechas a mano, cada salida nueva sería una oportunidad de que el vuelo no coincida con el viaje.
/// </summary>
public static class FlightDateCalculator
{
    public record FlightDates(DateOnly Outbound, DateOnly? Inbound);

    /// <param name="departureDate">Fecha de salida del paquete (PackageAvailability.DepartureDate).</param>
    /// <param name="durationDays">Duración del paquete en días.</param>
    public static FlightDates Calculate(PackageFlightRule rule, DateOnly departureDate, int durationDays)
    {
        var outbound = departureDate.AddDays(rule.OutboundOffsetDays);

        if (!rule.RoundTrip) return new FlightDates(outbound, null);

        // El último día del paquete es la salida más (duración - 1): un viaje de 7 días que sale el 19
        // termina el 25, no el 26.
        var lastDay = departureDate.AddDays(Math.Max(durationDays - 1, 0));
        var inbound = lastDay.AddDays(rule.InboundOffsetDays);

        // Una vuelta antes de la ida no es un viaje: se corrige al mismo día de la ida en vez de pedirle
        // al proveedor algo imposible.
        if (inbound < outbound) inbound = outbound;

        return new FlightDates(outbound, inbound);
    }

    /// <summary>Los tramos que se le piden al proveedor: uno si es sólo ida, dos si es ida y vuelta.</summary>
    public static IReadOnlyList<FlightSliceRequest> BuildSlices(
        PackageFlightRule rule, string originIata, FlightDates dates)
    {
        var origin = AirportCatalog.Normalize(originIata);
        var destination = AirportCatalog.Normalize(rule.DestinationIata);

        var slices = new List<FlightSliceRequest> { new(origin, destination, dates.Outbound) };

        if (dates.Inbound is { } inbound)
            slices.Add(new FlightSliceRequest(destination, origin, inbound));

        return slices;
    }
}
