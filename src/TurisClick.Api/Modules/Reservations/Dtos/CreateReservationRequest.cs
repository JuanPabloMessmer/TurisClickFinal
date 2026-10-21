using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TurisClick.Api.Modules.Flights.Dtos;
using TurisClick.Api.Modules.Flights.ProviderFlights.Dtos;

namespace TurisClick.Api.Modules.Reservations.Dtos;

/// <summary>
/// UC-T-08/UC-T-09 — Reservar una Experience individual o un Package de proveedor. Exactamente uno de
/// ExperienceAvailabilityId/PackageAvailabilityId debe venir presente (mismo invariante de forma que
/// ReservationItem/database-design.md ck_reservation_items_product_shape).
/// </summary>
public class CreateReservationRequest : IValidatableObject
{
    /// <summary>UC-T-08. Exactamente uno de este o PackageAvailabilityId.</summary>
    public Guid? ExperienceAvailabilityId { get; set; }

    /// <summary>UC-T-09. Exactamente uno de este o ExperienceAvailabilityId.</summary>
    public Guid? PackageAvailabilityId { get; set; }

    [Range(1, 100)]
    public int Travelers { get; set; }

    /// <summary>
    /// UC-T-21 — cotización de vuelo elegida, para un paquete que incluye pasaje. Es un id NUESTRO: el
    /// cliente nunca manda el identificador de la oferta del proveedor ni el precio. Sólo aplica a paquetes.
    ///
    /// Reservar dos veces la misma cotización no crea dos reservas: la segunda devuelve la que ya existe.
    /// </summary>
    public Guid? FlightQuoteId { get; set; }

    /// <summary>
    /// Vuelo suelto de operador: la salida de ida y, en ida y vuelta, la de vuelta, en ese orden (los
    /// <c>departureIds</c> que trae la búsqueda). Excluyente con experiencia y paquete. Con vuelo, Travelers
    /// es la cantidad de pasajeros con asiento (adultos + niños): los infantes viajan en brazos.
    /// </summary>
    public List<Guid>? FlightDepartureIds { get; set; }

    /// <summary>Los pasajeros del vuelo de operador. En la demo, datos sintéticos; sin documento de identidad.</summary>
    public List<ReservationPassengerRequest>? Passengers { get; set; }

    [JsonIgnore]
    public bool IsProviderFlight => FlightDepartureIds is { Count: > 0 };

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var hasExperience = ExperienceAvailabilityId.HasValue;
        var hasPackage = PackageAvailabilityId.HasValue;

        if (IsProviderFlight)
        {
            foreach (var error in ValidateProviderFlight(hasExperience || hasPackage))
                yield return error;
            yield break;
        }

        if (Passengers is { Count: > 0 })
            yield return new ValidationResult(
                "Los pasajeros sólo se indican al reservar un vuelo.", [nameof(Passengers)]);

        if (hasExperience == hasPackage)
            yield return new ValidationResult(
                "Debe indicarse exactamente uno de ExperienceAvailabilityId o PackageAvailabilityId.",
                [nameof(ExperienceAvailabilityId), nameof(PackageAvailabilityId)]);

        // Una experiencia individual no tiene vuelo: el vuelo es una regla del paquete.
        if (FlightQuoteId.HasValue && hasExperience)
            yield return new ValidationResult(
                "Sólo un paquete puede incluir vuelo.", [nameof(FlightQuoteId)]);
    }

    private IEnumerable<ValidationResult> ValidateProviderFlight(bool hasOtherProduct)
    {
        if (hasOtherProduct || FlightQuoteId.HasValue)
            yield return new ValidationResult(
                "Una reserva de vuelo no puede incluir también una experiencia, un paquete o una cotización de vuelo.",
                [nameof(FlightDepartureIds)]);

        if (FlightDepartureIds!.Count > 2 || FlightDepartureIds.Distinct().Count() != FlightDepartureIds.Count)
            yield return new ValidationResult(
                "Un vuelo se reserva con una salida (sólo ida) o dos distintas (ida y vuelta).", [nameof(FlightDepartureIds)]);

        var passengers = Passengers ?? [];
        if (passengers.Count == 0)
        {
            yield return new ValidationResult("Faltan los datos de los pasajeros.", [nameof(Passengers)]);
            yield break;
        }

        var adults = passengers.Count(p => p.Type == PassengerTypes.Adult);
        var infants = passengers.Count(p => p.Type == PassengerTypes.Infant);
        var seated = passengers.Count - infants;

        if (passengers.Count > SearchFlightsRequest.MaxPassengers)
            yield return new ValidationResult(
                $"Se pueden reservar hasta {SearchFlightsRequest.MaxPassengers} pasajeros, contando niños e infantes.", [nameof(Passengers)]);

        if (adults == 0)
            yield return new ValidationResult("Tiene que viajar al menos un adulto.", [nameof(Passengers)]);

        if (infants > adults)
            yield return new ValidationResult(
                "Cada infante viaja en brazos de un adulto: no puede haber más infantes que adultos.", [nameof(Passengers)]);

        if (Travelers != seated)
            yield return new ValidationResult(
                $"Travelers tiene que ser la cantidad de pasajeros con asiento ({seated}).", [nameof(Travelers)]);
    }
}
