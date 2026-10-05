using TurisClick.Api.Modules.Categories.Entities;
using TurisClick.Api.Modules.Companies.Entities;
using TurisClick.Api.Modules.Destinations.Entities;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Flights.Entities;

namespace TurisClick.Api.Modules.Packages.Entities;

/// <summary>docs/domain-model.md §5. Soporta UC-P-07/08/09/11, UC-T-06/07.</summary>
public class Package
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public Guid DestinationId { get; set; }
    public Destination? Destination { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ConditionsText { get; set; }

    /// <summary>Define el rango válido de PackageItem.DayNumber (1..DurationDays).</summary>
    public int DurationDays { get; set; }

    /// <summary>Precio comercial propio del paquete — NUNCA se deriva sumando sus PackageItem (decisión del dominio).</summary>
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;

    public PublicationStatus Status { get; set; } = PublicationStatus.DRAFT;

    /// <summary>
    /// Si el paquete incluye el aéreo. El precio del vuelo NO se guarda acá: es dinámico y se cotiza
    /// contra el proveedor cada vez (ver PackageFlightRule y docs/flight-integration-design.md).
    /// </summary>
    public bool IncludesFlight { get; set; }

    /// <summary>Regla con la que se busca el vuelo. Existe si y sólo si IncludesFlight es true.</summary>
    public PackageFlightRule? FlightRule { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<PackageItem> Items { get; set; } = new List<PackageItem>();
    public ICollection<PackageImage> Images { get; set; } = new List<PackageImage>();
    public ICollection<PackageAvailability> Availabilities { get; set; } = new List<PackageAvailability>();
    public ICollection<Category> Categories { get; set; } = new List<Category>();
}
