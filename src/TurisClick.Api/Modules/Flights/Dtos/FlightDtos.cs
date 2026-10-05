using System.ComponentModel.DataAnnotations;
using TurisClick.Api.Modules.Flights.Entities;
using TurisClick.Api.Modules.Flights.Services;

namespace TurisClick.Api.Modules.Flights.Dtos;

/// <summary>
/// Lo que el operador configura: la regla, nunca un vuelo. Si alguna vez aparece acá un campo
/// "aerolínea" o "precio del pasaje", algo se diseñó mal.
/// </summary>
public class PackageFlightRuleRequest : IValidatableObject
{
    [Required]
    public string DestinationIata { get; set; } = string.Empty;

    /// <summary>Uno o más orígenes que el operador acepta. El turista elige entre estos.</summary>
    [Required, MinLength(1)]
    public List<string> AllowedOriginIatas { get; set; } = [];

    /// <summary>
    /// String y no el enum directo, por la misma razón que PackageItemRequest.Kind: no hay un
    /// JsonStringEnumConverter global registrado, así que bindear un enum desde JSON daría 400.
    /// </summary>
    [Required, EnumDataType(typeof(FlightCabinClass))]
    public string CabinClass { get; set; } = nameof(FlightCabinClass.ECONOMY);

    [Range(-7, 7)]
    public int OutboundOffsetDays { get; set; }

    [Range(-7, 7)]
    public int InboundOffsetDays { get; set; }

    public bool RoundTrip { get; set; } = true;

    public FlightCabinClass Cabin() => Enum.Parse<FlightCabinClass>(CabinClass, ignoreCase: true);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!AirportCatalog.IsValidCode(AirportCatalog.Normalize(DestinationIata)))
            yield return new ValidationResult(
                "El aeropuerto de destino tiene que ser un código IATA de tres letras.", [nameof(DestinationIata)]);

        foreach (var origin in AllowedOriginIatas)
        {
            if (!AirportCatalog.IsValidCode(AirportCatalog.Normalize(origin)))
            {
                yield return new ValidationResult(
                    $"'{origin}' no es un código IATA válido.", [nameof(AllowedOriginIatas)]);
                yield break;
            }
        }

        var normalized = AllowedOriginIatas.Select(AirportCatalog.Normalize).ToList();

        if (normalized.Distinct().Count() != normalized.Count)
            yield return new ValidationResult("Hay aeropuertos de origen repetidos.", [nameof(AllowedOriginIatas)]);

        // Un vuelo que sale y llega al mismo lugar no existe; es un error de carga, no una opción.
        if (normalized.Contains(AirportCatalog.Normalize(DestinationIata)))
            yield return new ValidationResult(
                "El destino no puede estar también entre los orígenes permitidos.", [nameof(AllowedOriginIatas)]);
    }
}

public class PackageFlightRuleResponse
{
    public Guid PackageId { get; set; }
    public string DestinationIata { get; set; } = string.Empty;
    public string DestinationLabel { get; set; } = string.Empty;
    public List<AirportResponse> AllowedOrigins { get; set; } = [];
    public string CabinClass { get; set; } = string.Empty;
    public int OutboundOffsetDays { get; set; }
    public int InboundOffsetDays { get; set; }
    public bool RoundTrip { get; set; }
}

public class AirportResponse
{
    public string Iata { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

/// <summary>Lo que el turista pide para cotizar: desde dónde sale, para qué salida y cuántos son.</summary>
public class PackageFlightQuoteRequest
{
    [Required]
    public string OriginIata { get; set; } = string.Empty;

    [Required]
    public Guid PackageAvailabilityId { get; set; }

    [Range(1, 9)]
    public int Travelers { get; set; } = 1;
}

/// <summary>
/// Respuesta de una cotización: el precio del paquete, las opciones de vuelo verificadas contra el
/// proveedor y los totales. Los totales NO se combinan si las monedas difieren — la regla de
/// multi-moneda del dominio no se rompe por comodidad de la pantalla.
/// </summary>
public class PackageFlightQuoteResponse
{
    public Guid PackageId { get; set; }
    public string PackageTitle { get; set; } = string.Empty;
    public int Travelers { get; set; }

    public MoneyResponse PackagePrice { get; set; } = new();

    public string OriginIata { get; set; } = string.Empty;
    public string OriginLabel { get; set; } = string.Empty;
    public string DestinationIata { get; set; } = string.Empty;
    public string DestinationLabel { get; set; } = string.Empty;

    public DateOnly OutboundDate { get; set; }
    public DateOnly? InboundDate { get; set; }
    public string CabinClass { get; set; } = string.Empty;

    public List<FlightQuoteOptionResponse> Options { get; set; } = [];

    /// <summary>Aviso cuando el proveedor no devolvió nada: la pantalla lo muestra en vez de quedar vacía.</summary>
    public string? Notice { get; set; }

    /// <summary>true si las ofertas son de un entorno de prueba. La app lo rotula; nunca se disimula.</summary>
    public bool TestMode { get; set; }
}

public class FlightQuoteOptionResponse
{
    /// <summary>Id de la cotización en TurisClick. Es lo que el cliente manda para revalidar; el id del proveedor no sale de acá.</summary>
    public Guid QuoteId { get; set; }

    public MoneyResponse FlightPrice { get; set; } = new();

    /// <summary>Paquete + vuelo, sólo si comparten moneda. Si no, queda nulo y la app muestra los dos importes por separado.</summary>
    public MoneyResponse? CombinedTotal { get; set; }

    public string? CarrierName { get; set; }
    public string? CarrierIata { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public List<FlightSliceResponse> Slices { get; set; } = [];
}

public class FlightSliceResponse
{
    public string OriginIata { get; set; } = string.Empty;
    public string DestinationIata { get; set; } = string.Empty;
    public int? DurationMinutes { get; set; }

    /// <summary>Escalas = segmentos - 1. Cero significa vuelo directo.</summary>
    public int Stops { get; set; }

    public List<FlightSegmentResponse> Segments { get; set; } = [];
}

public class FlightSegmentResponse
{
    public string OriginIata { get; set; } = string.Empty;
    public string DestinationIata { get; set; } = string.Empty;

    /// <summary>Hora local del aeropuerto, sin huso: así es como la publican las aerolíneas.</summary>
    public DateTime DepartingAt { get; set; }
    public DateTime ArrivingAt { get; set; }

    public string? CarrierIata { get; set; }
    public string? CarrierName { get; set; }
    public string? FlightNumber { get; set; }
    public int? CheckedBags { get; set; }
}

public class MoneyResponse
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

/// <summary>Resultado de volver a preguntarle al proveedor por una cotización ya mostrada.</summary>
public class FlightQuoteRevalidationResponse
{
    public Guid QuoteId { get; set; }

    /// <summary>UNCHANGED | PRICE_CHANGED | EXPIRED | UNAVAILABLE</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>El precio que se le mostró a la persona cuando cotizó.</summary>
    public MoneyResponse PreviousPrice { get; set; } = new();

    /// <summary>El precio vigente ahora. Igual al anterior cuando no cambió; nulo si ya no hay oferta.</summary>
    public MoneyResponse? CurrentPrice { get; set; }

    public MoneyResponse? CombinedTotal { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Qué decirle a la persona. El dominio decide el texto, no la pantalla.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>true cuando hace falta que acepte explícitamente el precio nuevo antes de seguir.</summary>
    public bool RequiresAcceptance { get; set; }
}

/// <summary>
/// Un pasajero, con lo que la oferta exige y nada más.
///
/// Esta lista no sale de "lo que suele pedir una aerolínea": sale de lo que el proveedor valida para
/// emitir. No hay pasaporte ni documento acá a propósito —un vuelo doméstico no lo necesita, y si una
/// oferta lo exigiera el flujo lo dice y no la vende, en vez de pedirle a todo el mundo datos sensibles
/// por las dudas.
///
/// **Nada de esto se persiste.** Se valida, se manda al proveedor y se descarta; tampoco entra en ningún
/// log. Ver <see cref="Entities.FlightBooking"/>.
/// </summary>
public class FlightTravelerRequest
{
    /// <summary>
    /// Sin dígitos, y la restricción no es estética: el proveedor rechaza la orden entera si un nombre
    /// trae números, así que se corta acá con un mensaje entendible en vez de allá con un 422.
    /// </summary>
    [Required, StringLength(50, MinimumLength = 2)]
    [RegularExpression(@"^\p{L}[\p{L} '\-\.]*$", ErrorMessage = "El nombre sólo puede tener letras, espacios, apóstrofos y guiones.")]
    public string GivenName { get; set; } = string.Empty;

    [Required, StringLength(50, MinimumLength = 2)]
    [RegularExpression(@"^\p{L}[\p{L} '\-\.]*$", ErrorMessage = "El apellido sólo puede tener letras, espacios, apóstrofos y guiones.")]
    public string FamilyName { get; set; } = string.Empty;

    [Required]
    public DateOnly BornOn { get; set; }

    /// <summary>"m" o "f": es lo que el proveedor acepta hoy. Se mapea en el adapter, no se interpreta acá.</summary>
    [Required, RegularExpression("^[mf]$", ErrorMessage = "Indicá 'm' o 'f'.")]
    public string Gender { get; set; } = string.Empty;

    [Required, RegularExpression("^(mr|ms|mrs|miss|dr)$", ErrorMessage = "Tratamiento no reconocido.")]
    public string Title { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(150)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Formato internacional (+591…): es el único que la aerolínea puede usar para avisar un cambio.</summary>
    [Required, RegularExpression(@"^\+[1-9]\d{6,14}$", ErrorMessage = "Escribí el teléfono en formato internacional, por ejemplo +59170000000.")]
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>Importe que el cliente declara haber aceptado. Se compara contra el precio vigente; no se confía en él.</summary>
public class MoneyRequest
{
    [Range(0, 1_000_000)]
    public decimal Amount { get; set; }

    [Required, RegularExpression("^[A-Z]{3}$")]
    public string Currency { get; set; } = string.Empty;
}

/// <summary>
/// El vuelo de una reserva, tal como se lo muestra a su dueño. Nunca lleva el id de la oferta ni el de la
/// orden del proveedor: lo que la persona necesita es el localizador, y lo que la app necesita es el
/// estado.
/// </summary>
public class FlightBookingResponse
{
    /// <summary>PENDING | ORDERING | CONFIRMED | FAILED | RECONCILIATION_REQUIRED | CANCELLED</summary>
    public string Status { get; set; } = string.Empty;

    public string OriginIata { get; set; } = string.Empty;
    public string OriginLabel { get; set; } = string.Empty;
    public string DestinationIata { get; set; } = string.Empty;
    public string DestinationLabel { get; set; } = string.Empty;

    public DateOnly OutboundDate { get; set; }
    public DateOnly? InboundDate { get; set; }
    public int Travelers { get; set; }

    public string? CarrierIata { get; set; }
    public string? CarrierName { get; set; }

    public DateTime? OutboundDepartureAt { get; set; }
    public DateTime? OutboundArrivalAt { get; set; }
    public string? OutboundFlightNumber { get; set; }

    public DateTime? InboundDepartureAt { get; set; }
    public DateTime? InboundArrivalAt { get; set; }
    public string? InboundFlightNumber { get; set; }

    /// <summary>Localizador de la aerolínea. Sólo existe cuando la orden está confirmada.</summary>
    public string? BookingReference { get; set; }

    public MoneyResponse Price { get; set; } = new();

    public string ItinerarySummary { get; set; } = string.Empty;

    /// <summary>Qué está pasando con el vuelo, en palabras, para mostrarlo sin que la app interprete estados.</summary>
    public string StatusMessage { get; set; } = string.Empty;

    /// <summary>true mientras el desenlace esté sin resolver: la app vuelve a consultar en vez de ofrecer reintentar.</summary>
    public bool InProgress { get; set; }
}
