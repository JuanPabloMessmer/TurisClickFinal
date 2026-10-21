using System.ComponentModel.DataAnnotations;
using TurisClick.Api.Modules.Flights.Services;
using TurisClick.Api.Modules.Reservations.Dtos;
using TurisClick.Api.Modules.Reservations.Policies;

namespace TurisClick.Api.Modules.Flights.ProviderFlights.Dtos;

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────
// Vuelos de operador (Workstream B). Contratos acordados en docs/vuelos-sueltos-contratos.md.
//
// B1: estos tipos y las firmas de los endpoints existen para que el Backoffice y la app trabajen contra los
// tipos generados. La lógica, las entidades y la migración 0015 llegan en B2; hasta entonces los endpoints
// responden 501 con un mensaje claro.
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>Valores de <c>ProviderFlightResponse.Status</c>.</summary>
public static class ProviderFlightStatuses
{
    /// <summary>Recién creado o en edición: no aparece en la búsqueda.</summary>
    public const string Draft = "DRAFT";

    /// <summary>A la venta: sus salidas abiertas con asientos aparecen en la búsqueda.</summary>
    public const string Published = "PUBLISHED";

    /// <summary>Retirado por el operador. Las reservas ya hechas siguen su curso.</summary>
    public const string Archived = "ARCHIVED";
}

/// <summary>Valores de <c>ProviderFlightDepartureResponse.Status</c>.</summary>
public static class ProviderFlightDepartureStatuses
{
    public const string Open = "OPEN";

    /// <summary>El operador dejó de vender esta salida. Las reservas hechas se mantienen.</summary>
    public const string Closed = "CLOSED";

    public const string Cancelled = "CANCELLED";
}

/// <summary>
/// Emisión del pasaje de un ítem de vuelo de operador. **"Pasaje emitido" sólo existe en ISSUED**, y ISSUED
/// sólo lo escribe el operador al registrar la emisión: TurisClick no emite pasajes de operador.
/// </summary>
public static class FlightIssuanceStatuses
{
    /// <summary>No hay emisión en curso: ítems que no son vuelos de operador, o vuelos todavía sin pagar.</summary>
    public const string NotRequired = "NOT_REQUIRED";

    /// <summary>Pagado: el operador tiene que emitir y registrarlo.</summary>
    public const string Pending = "PENDING";

    /// <summary>El operador registró la emisión con sus números de pasaje.</summary>
    public const string Issued = "ISSUED";

    /// <summary>El operador no pudo emitir: el ítem se cancela con reembolso total.</summary>
    public const string Failed = "FAILED";
}

/// <summary>Tipos de pasajero de una reserva de vuelo de operador.</summary>
public static class PassengerTypes
{
    public const string Adult = "ADULT";
    public const string Child = "CHILD";
    public const string Infant = "INFANT";
}

// ---------------------------------------------------------------- operador: el vuelo

/// <summary>
/// Un vuelo que el operador vende: la ruta, la aerolínea que lo opera, el número de vuelo, la cabina, el
/// equipaje y la política de cancelación. Las fechas, los asientos y las tarifas van en cada salida.
///
/// Mismas reglas de ruta que la búsqueda: origen en cualquier aeropuerto conocido y destino en Bolivia, sin
/// que sean el mismo aeropuerto ni la misma ciudad.
/// </summary>
public class ProviderFlightRequest : IValidatableObject
{
    [Required, StringLength(3, MinimumLength = 3)]
    public string OriginIata { get; set; } = string.Empty;

    [Required, StringLength(3, MinimumLength = 3)]
    public string DestinationIata { get; set; } = string.Empty;

    /// <summary>Aerolínea que opera el vuelo, como se le muestra al turista.</summary>
    [Required, StringLength(100, MinimumLength = 2)]
    public string CarrierName { get; set; } = string.Empty;

    /// <summary>Código IATA de la aerolínea, si tiene (dos caracteres).</summary>
    [RegularExpression("^[A-Z0-9]{2}$", ErrorMessage = "El código de aerolínea tiene dos letras o números, por ejemplo OB.")]
    public string? CarrierIata { get; set; }

    [Required, RegularExpression("^[0-9]{1,4}[A-Z]?$", ErrorMessage = "El número de vuelo son hasta cuatro dígitos, por ejemplo 712.")]
    public string FlightNumber { get; set; } = string.Empty;

    [EnumDataType(typeof(FlightCabinClass))]
    public string CabinClass { get; set; } = nameof(FlightCabinClass.ECONOMY);

    /// <summary>Valijas despachadas incluidas por pasajero con asiento.</summary>
    [Range(0, 3)]
    public int CheckedBags { get; set; }

    /// <summary>Equipaje de mano incluido por pasajero con asiento.</summary>
    [Range(0, 2)]
    public int CarryOnBags { get; set; } = 1;

    /// <summary>Misma forma que en experiencias y paquetes. Vacía = no se cancela desde la app.</summary>
    public List<CancellationTierDto>? CancellationPolicy { get; set; }

    /// <summary>Condiciones de la tarifa en texto libre (cambios, no-show), tal como el operador las publica.</summary>
    [StringLength(2000)]
    public string? FareRules { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var origin = AirportCatalog.Normalize(OriginIata);
        var destination = AirportCatalog.Normalize(DestinationIata);

        if (AirportCatalog.Find(origin) is null)
            yield return new ValidationResult($"No conocemos el aeropuerto {origin}.", [nameof(OriginIata)]);

        if (!AirportCatalog.IsBolivianDestination(destination))
            yield return new ValidationResult(
                "Por ahora los vuelos tienen que llegar a un aeropuerto de Bolivia con vuelos regulares.",
                [nameof(DestinationIata)]);

        if (origin == destination)
            yield return new ValidationResult(
                "El origen y el destino no pueden ser el mismo aeropuerto.", [nameof(DestinationIata)]);
        else if (AirportCatalog.SameCity(origin, destination))
            yield return new ValidationResult("El origen y el destino están en la misma ciudad.", [nameof(DestinationIata)]);

        foreach (var error in CancellationPolicyRules.Validate(CancellationPolicy))
            yield return new ValidationResult(error, [nameof(CancellationPolicy)]);
    }
}

public class ProviderFlightResponse
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;

    public string OriginIata { get; set; } = string.Empty;
    public string OriginLabel { get; set; } = string.Empty;
    public string DestinationIata { get; set; } = string.Empty;
    public string DestinationLabel { get; set; } = string.Empty;

    public string CarrierName { get; set; } = string.Empty;
    public string? CarrierIata { get; set; }
    public string FlightNumber { get; set; } = string.Empty;
    public string CabinClass { get; set; } = string.Empty;
    public int CheckedBags { get; set; }
    public int CarryOnBags { get; set; }
    public List<CancellationTierDto> CancellationPolicy { get; set; } = [];
    public string? FareRules { get; set; }

    /// <summary>DRAFT, PUBLISHED o ARCHIVED (ver <see cref="ProviderFlightStatuses"/>).</summary>
    public string Status { get; set; } = ProviderFlightStatuses.Draft;

    /// <summary>Suspendido por un ADMIN: no aparece en la búsqueda aunque esté publicado.</summary>
    public bool Suspended { get; set; }

    /// <summary>Salidas futuras abiertas con al menos un asiento libre.</summary>
    public int UpcomingDepartures { get; set; }

    /// <summary>true en el entorno de demostración: lo que se vende no es un pasaje real. Las salidas heredan el rótulo.</summary>
    public bool Demo { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

// ---------------------------------------------------------------- operador: las salidas

/// <summary>
/// Una salida concreta: fecha y hora, asientos y tarifas por tipo de pasajero. Los horarios son **hora local
/// del aeropuerto, sin huso**, igual que en el resto del módulo de vuelos.
/// </summary>
public class ProviderFlightDepartureRequest : IValidatableObject
{
    public const int MaxDaysAhead = 330;
    public const int MaxDurationHours = 48;

    public DateTime DepartureAt { get; set; }
    public DateTime ArrivalAt { get; set; }

    [Range(1, 500)]
    public int TotalSeats { get; set; }

    [Range(typeof(decimal), "0", "1000000")]
    public decimal AdultPrice { get; set; }

    /// <summary>Niños de 2 a 11 años, con asiento.</summary>
    [Range(typeof(decimal), "0", "1000000")]
    public decimal ChildPrice { get; set; }

    /// <summary>Infantes menores de 2, en brazos: no ocupan asiento.</summary>
    [Range(typeof(decimal), "0", "1000000")]
    public decimal InfantPrice { get; set; }

    [Required, RegularExpression("^[A-Z]{3}$", ErrorMessage = "La moneda es un código ISO de tres letras, por ejemplo BOB o USD.")]
    public string Currency { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ArrivalAt <= DepartureAt)
            yield return new ValidationResult("La llegada tiene que ser posterior a la salida.", [nameof(ArrivalAt)]);
        else if (ArrivalAt - DepartureAt > TimeSpan.FromHours(MaxDurationHours))
            yield return new ValidationResult($"Un vuelo no puede durar más de {MaxDurationHours} horas.", [nameof(ArrivalAt)]);

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var departureDate = DateOnly.FromDateTime(DepartureAt);
        if (departureDate < today)
            yield return new ValidationResult("La salida ya pasó.", [nameof(DepartureAt)]);
        else if (departureDate > today.AddDays(MaxDaysAhead))
            yield return new ValidationResult(
                $"Las salidas se publican hasta {MaxDaysAhead} días antes.", [nameof(DepartureAt)]);
    }
}

public class ProviderFlightDepartureResponse
{
    public Guid Id { get; set; }
    public Guid ProviderFlightId { get; set; }

    public DateTime DepartureAt { get; set; }
    public DateTime ArrivalAt { get; set; }

    public int TotalSeats { get; set; }
    public int AvailableSeats { get; set; }

    public decimal AdultPrice { get; set; }
    public decimal ChildPrice { get; set; }
    public decimal InfantPrice { get; set; }
    public string Currency { get; set; } = string.Empty;

    /// <summary>OPEN, CLOSED o CANCELLED (ver <see cref="ProviderFlightDepartureStatuses"/>).</summary>
    public string Status { get; set; } = ProviderFlightDepartureStatuses.Open;

    /// <summary>true si alguna reserva la usa: entonces no se borra, se cierra.</summary>
    public bool HasReservations { get; set; }
}

// ---------------------------------------------------------------- operador: la emisión

/// <summary>
/// El operador emitió el pasaje fuera de TurisClick y lo registra acá. Es lo único que hace que la app diga
/// "Pasaje emitido". Un número de pasaje por pasajero, en el orden en que figuran en la reserva.
/// </summary>
public class RecordIssuanceRequest : IValidatableObject
{
    [Required, MinLength(1), MaxLength(9)]
    public List<string> TicketNumbers { get; set; } = [];

    /// <summary>Localizador de la reserva en la aerolínea, si el operador lo tiene.</summary>
    [RegularExpression("^[A-Za-z0-9]{5,8}$", ErrorMessage = "El localizador tiene de 5 a 8 letras o números.")]
    public string? BookingReference { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TicketNumbers.Any(t => string.IsNullOrWhiteSpace(t) || t.Trim().Length is < 6 or > 20))
            yield return new ValidationResult(
                "Cada número de pasaje tiene entre 6 y 20 caracteres.", [nameof(TicketNumbers)]);

        if (TicketNumbers.Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != TicketNumbers.Count)
            yield return new ValidationResult("Hay números de pasaje repetidos.", [nameof(TicketNumbers)]);
    }
}

/// <summary>El operador no pudo emitir: el ítem se cancela con reembolso total y se liberan los asientos.</summary>
public class IssuanceFailureRequest
{
    [Required, StringLength(500, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;
}

public class FlightItemIssuanceResponse
{
    public Guid ReservationItemId { get; set; }

    /// <summary>NOT_REQUIRED, PENDING, ISSUED o FAILED (ver <see cref="FlightIssuanceStatuses"/>).</summary>
    public string IssuanceStatus { get; set; } = FlightIssuanceStatuses.Pending;

    public List<string> TicketNumbers { get; set; } = [];
    public string? BookingReference { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public string? Note { get; set; }
}

// ---------------------------------------------------------------- admin

/// <summary>
/// Autorizar o revocar a una empresa para vender vuelos. Autorizar exige el registro o licencia que declaró
/// (texto, tal como figura en su documentación): es la constancia de por qué se la autorizó.
/// </summary>
public class FlightSellerAuthorizationRequest : IValidatableObject
{
    public bool Authorized { get; set; }

    [StringLength(100)]
    public string? Registration { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Authorized && string.IsNullOrWhiteSpace(Registration))
            yield return new ValidationResult(
                "Para autorizar la venta de vuelos hay que indicar el registro o la licencia de la empresa.",
                [nameof(Registration)]);
    }
}

public class FlightSellerAuthorizationResponse
{
    public Guid CompanyId { get; set; }
    public bool CanSellFlights { get; set; }
    public string? Registration { get; set; }
    public DateTimeOffset? AuthorizedAt { get; set; }
    public Guid? AuthorizedBy { get; set; }
}

// ---------------------------------------------------------------- turista: reserva y lectura

/// <summary>
/// Un pasajero de una reserva de vuelo de operador. En la demo, **sólo datos sintéticos**: no se pide ni se
/// guarda documento de identidad. El operador necesita nombre, apellido y fecha de nacimiento para emitir.
/// </summary>
public class ReservationPassengerRequest : IValidatableObject
{
    [Required, RegularExpression("^(ADULT|CHILD|INFANT)$", ErrorMessage = "Tipo de pasajero no reconocido.")]
    public string Type { get; set; } = PassengerTypes.Adult;

    [Required, StringLength(60, MinimumLength = 1)]
    [RegularExpression(@"^\p{L}[\p{L} '\-\.]*$", ErrorMessage = "El nombre sólo puede tener letras, espacios, apóstrofos y guiones.")]
    public string GivenName { get; set; } = string.Empty;

    [Required, StringLength(60, MinimumLength = 1)]
    [RegularExpression(@"^\p{L}[\p{L} '\-\.]*$", ErrorMessage = "El apellido sólo puede tener letras, espacios, apóstrofos y guiones.")]
    public string FamilyName { get; set; } = string.Empty;

    public DateOnly BornOn { get; set; }

    /// <summary>
    /// Sólo se valida lo que no depende del vuelo (que la fecha sea pasada y razonable). Que la edad
    /// corresponda al tipo **el día del vuelo** lo valida la reserva, que conoce la fecha de salida.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        if (BornOn >= today)
            yield return new ValidationResult("La fecha de nacimiento tiene que ser pasada.", [nameof(BornOn)]);
        else if (BornOn < today.AddYears(-120))
            yield return new ValidationResult("Revisá la fecha de nacimiento.", [nameof(BornOn)]);
    }
}

public class ReservationPassengerResponse
{
    public string Type { get; set; } = PassengerTypes.Adult;
    public string GivenName { get; set; } = string.Empty;
    public string FamilyName { get; set; } = string.Empty;
    public DateOnly BornOn { get; set; }
}

/// <summary>
/// El vuelo de operador de un ítem de reserva: lo que el turista compró y en qué estado está la emisión.
/// Se congela al reservar, igual que el resto de los ítems.
/// </summary>
public class ProviderFlightItemResponse
{
    public Guid ProviderFlightId { get; set; }
    public Guid DepartureId { get; set; }

    public string OriginIata { get; set; } = string.Empty;
    public string OriginLabel { get; set; } = string.Empty;
    public string DestinationIata { get; set; } = string.Empty;
    public string DestinationLabel { get; set; } = string.Empty;

    public DateTime DepartureAt { get; set; }
    public DateTime ArrivalAt { get; set; }

    public string CarrierName { get; set; } = string.Empty;
    public string? CarrierIata { get; set; }
    public string FlightNumber { get; set; } = string.Empty;
    public string CabinClass { get; set; } = string.Empty;
    public int CheckedBags { get; set; }
    public int CarryOnBags { get; set; }

    /// <summary>La empresa que vende el vuelo y es responsable de emitirlo.</summary>
    public string OperatorName { get; set; } = string.Empty;

    /// <summary>NOT_REQUIRED, PENDING, ISSUED o FAILED (ver <see cref="FlightIssuanceStatuses"/>).</summary>
    public string IssuanceStatus { get; set; } = FlightIssuanceStatuses.Pending;

    public List<string> TicketNumbers { get; set; } = [];
    public string? BookingReference { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }

    public List<ReservationPassengerResponse> Passengers { get; set; } = [];

    /// <summary>
    /// true en el entorno de demostración: la app rotula DEMO todo vuelo, emitido o no, porque un número de
    /// pasaje cargado por un operador de prueba no es un pasaje real.
    /// </summary>
    public bool Demo { get; set; } = true;
}

// ---------------------------------------------------------------- turista: búsqueda

/// <summary>De dónde sale una opción de la búsqueda: <c>FlightOptionResponse.Source</c>.</summary>
public static class FlightOfferSources
{
    /// <summary>Oferta de aerolínea vía proveedor aéreo (Fake o Duffel). Hoy nunca reservable suelta.</summary>
    public const string Airline = "AIRLINE";

    /// <summary>Vuelo publicado por un operador autorizado. Reservable con sesión.</summary>
    public const string Provider = "PROVIDER";
}

/// <summary>
/// Lo que la búsqueda agrega a una opción que es un vuelo de operador: con qué salidas se reserva (ida y,
/// si corresponde, vuelta), quién lo vende y cuántos asientos quedan.
/// </summary>
public class ProviderFlightOfferResponse
{
    /// <summary>Una salida (sólo ida) o dos (ida y vuelta), en ese orden. Es lo que se manda al reservar.</summary>
    public List<Guid> DepartureIds { get; set; } = [];

    public Guid OperatorCompanyId { get; set; }
    public string OperatorName { get; set; } = string.Empty;

    /// <summary>El mínimo de asientos libres entre las salidas de la opción.</summary>
    public int SeatsLeft { get; set; }

    public List<CancellationTierDto> CancellationPolicy { get; set; } = [];
    public string? FareRules { get; set; }
}

/// <summary>Página de vuelos de operador para el ADMIN.</summary>
public class AdminProviderFlightsQuery
{
    public Guid? CompanyId { get; set; }

    [RegularExpression("^(DRAFT|PUBLISHED|ARCHIVED)$")]
    public string? Status { get; set; }

    public bool? Suspended { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}
