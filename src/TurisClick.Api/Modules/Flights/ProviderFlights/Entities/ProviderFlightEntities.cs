using TurisClick.Api.Modules.Companies.Entities;
using TurisClick.Api.Modules.Flights.Services;
using TurisClick.Api.Modules.Reservations.Entities;

namespace TurisClick.Api.Modules.Flights.ProviderFlights.Entities;

/// <summary>
/// Un vuelo que vende una empresa autorizada: la ruta, la aerolínea que lo opera, el número de vuelo, la
/// cabina, el equipaje y la política de cancelación. Las fechas, los asientos y las tarifas viven en cada
/// <see cref="ProviderFlightDeparture"/>.
///
/// Es un producto del catálogo, no una oferta de proveedor aéreo: tiene dueño (<see cref="CompanyId"/>) y
/// cupo propio, así que se reserva como cualquier otro ítem —con el aislamiento por empresa de siempre— y lo
/// emite el operador, fuera de TurisClick.
///
/// Lo que se vendió no cambia por debajo: con reservas, la ruta, la aerolínea, el número de vuelo, la cabina
/// y el equipaje ya no se pueden editar (ver ProviderFlightService). La política se congela en cada ítem.
/// </summary>
public class ProviderFlight
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public string OriginIata { get; set; } = string.Empty;
    public string DestinationIata { get; set; } = string.Empty;

    public string CarrierName { get; set; } = string.Empty;
    public string? CarrierIata { get; set; }
    public string FlightNumber { get; set; } = string.Empty;

    public FlightCabinClass CabinClass { get; set; } = FlightCabinClass.ECONOMY;

    public int CheckedBags { get; set; }
    public int CarryOnBags { get; set; } = 1;

    /// <summary>Serializada igual que en experiencias y paquetes (`"7:100;0:0"`). Nula = no se cancela desde la app.</summary>
    public string? CancellationPolicy { get; set; }

    public string? FareRules { get; set; }

    public ProviderFlightStatus Status { get; set; } = ProviderFlightStatus.DRAFT;

    /// <summary>Suspendido por un ADMIN: sale de la búsqueda aunque esté publicado. Las reservas hechas siguen.</summary>
    public DateTimeOffset? SuspendedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<ProviderFlightDeparture> Departures { get; set; } = new List<ProviderFlightDeparture>();
}

public enum ProviderFlightStatus
{
    DRAFT,
    PUBLISHED,
    ARCHIVED,
}

/// <summary>
/// Una salida concreta del vuelo. Los horarios son **hora local del aeropuerto, sin huso**, igual que en el
/// resto del módulo de vuelos: "sale 08:15 de El Alto" significa 08:15 en El Alto.
///
/// El cupo sigue el patrón del resto del catálogo: un contador de asientos tomados (<see cref="ReservedSeats"/>)
/// que sólo se mueve con un UPDATE condicional, nunca leyendo y escribiendo. Los infantes no ocupan asiento.
/// </summary>
public class ProviderFlightDeparture
{
    public Guid Id { get; set; }

    public Guid ProviderFlightId { get; set; }
    public ProviderFlight? ProviderFlight { get; set; }

    public DateTime DepartureAt { get; set; }
    public DateTime ArrivalAt { get; set; }

    public int TotalSeats { get; set; }

    /// <summary>Asientos retenidos (reservas sin pagar) o vendidos. Libres = TotalSeats - ReservedSeats.</summary>
    public int ReservedSeats { get; set; }

    public decimal AdultPrice { get; set; }
    public decimal ChildPrice { get; set; }
    public decimal InfantPrice { get; set; }
    public string Currency { get; set; } = string.Empty;

    public ProviderFlightDepartureStatus Status { get; set; } = ProviderFlightDepartureStatus.OPEN;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public int AvailableSeats => Math.Max(TotalSeats - ReservedSeats, 0);
}

public enum ProviderFlightDepartureStatus
{
    OPEN,
    CLOSED,
    CANCELLED,
}

/// <summary>
/// Un pasajero de un ítem de vuelo de operador. Lo mínimo que el operador necesita para emitir: nombre,
/// apellido y fecha de nacimiento. **Sin documento de identidad**, y en la demo, sólo datos sintéticos.
///
/// Se guarda (a diferencia del vuelo de proveedor aéreo, donde los datos se envían y se descartan) porque el
/// que emite es el operador, después del pago, y necesita la lista. En ida y vuelta, cada tramo lleva la suya.
/// </summary>
public class ReservationPassenger
{
    public Guid Id { get; set; }

    public Guid ReservationItemId { get; set; }
    public ReservationItem? ReservationItem { get; set; }

    public PassengerType Type { get; set; } = PassengerType.ADULT;
    public string GivenName { get; set; } = string.Empty;
    public string FamilyName { get; set; } = string.Empty;
    public DateOnly BornOn { get; set; }

    /// <summary>El orden en que se cargaron: es el orden de los números de pasaje que registra el operador.</summary>
    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public enum PassengerType
{
    ADULT,
    CHILD,
    INFANT,
}
