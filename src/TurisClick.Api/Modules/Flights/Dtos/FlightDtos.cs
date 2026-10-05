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
