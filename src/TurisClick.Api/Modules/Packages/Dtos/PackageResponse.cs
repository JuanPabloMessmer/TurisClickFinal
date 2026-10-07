using TurisClick.Api.Modules.Categories.Dtos;

using TurisClick.Api.Modules.Reservations.Dtos;

namespace TurisClick.Api.Modules.Packages.Dtos;

/// <summary>Vista completa — usada por el detalle público (UC-T-07, solo PUBLISHED) y por el Provider dueño (cualquier estado).</summary>
public class PackageResponse
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public Guid DestinationId { get; set; }
    public string DestinationName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ConditionsText { get; set; }
    public int DurationDays { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Si el paquete incluye aéreo. El precio del vuelo NO viene acá: es dinámico y se pide aparte
    /// (POST /api/packages/{id}/flight-quotes), porque hasta no cotizar no existe.
    /// </summary>
    public bool IncludesFlight { get; set; }

    /// <summary>Aeropuerto al que vuela el paquete. Nulo si no incluye vuelo.</summary>
    public string? FlightDestinationIata { get; set; }
    public string? FlightDestinationLabel { get; set; }

    /// <summary>
    /// Desde dónde acepta salir el operador. Es lo único de la regla que el turista necesita ver: los
    /// desfases de fecha son configuración interna y no salen al catálogo público.
    /// </summary>
    public List<FlightOriginResponse> FlightOrigins { get; set; } = [];

    /// <summary>
    /// Política de cancelación del operador, por tramos. Lista vacía = el operador no definió ninguna, y una
    /// reserva confirmada de este paquete no se cancela desde la app.
    /// </summary>
    public List<CancellationTierDto> CancellationPolicy { get; set; } = [];

    public bool? FlightRoundTrip { get; set; }
    public List<CategoryResponse> Categories { get; set; } = [];
    public List<PackageImageResponse> Images { get; set; } = [];

    /// <summary>Agrupados por día (DayNumber) y ordenados por SortOrder dentro de cada día.</summary>
    public List<PackageItemResponse> Items { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class PackageImageResponse
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsCover { get; set; }
}

public class PackageItemResponse
{
    public Guid Id { get; set; }
    public int DayNumber { get; set; }
    public int SortOrder { get; set; }
    public string Kind { get; set; } = string.Empty;
    public Guid? ExperienceId { get; set; }

    /// <summary>Si Kind = EXPERIENCE_REFERENCE y no se pisó Title, muestra el título real de la Experience.</summary>
    public string? Title { get; set; }
    public string? Description { get; set; }
}

/// <summary>Un aeropuerto de salida habilitado, con su etiqueta legible.</summary>
public class FlightOriginResponse
{
    public string Iata { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}
