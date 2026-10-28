using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Companies.Entities;
using TurisClick.Api.Modules.Flights.Dtos;
using TurisClick.Api.Modules.Flights.ProviderFlights.Dtos;
using TurisClick.Api.Modules.Flights.ProviderFlights.Entities;
using TurisClick.Api.Modules.Reservations.Policies;
using TurisClick.Api.Modules.Packages.Entities;
using TurisClick.Api.Modules.Experiences.Entities;

namespace TurisClick.Api.Modules.Flights.Services;

public interface IFlightSearchService
{
    Task<SearchFlightsResponse> SearchAsync(SearchFlightsRequest request, CancellationToken ct);
}

/// <summary>
/// Búsqueda de vuelos sin paquete: el buscador de la app, un **metabuscador** desde la Oleada 19. **No escribe nada.**
///
/// <list type="bullet">
/// <item><b>Ofertas de aerolínea</b> (Source AIRLINE, nunca reservables en TurisClick): las del proveedor de
/// búsqueda (hoy el simulado). Cada una lleva <c>Redirect</c>: si se puede abrir en el sitio del vendedor
/// (FlightRedirectPolicy). No llevan id de cotización, porque desde acá no se compran.</item>
/// <item><b>Vuelos de operador</b> (Source PROVIDER, Oleada 18): sólo con <c>Flights:ProviderFlights:Enabled</c>,
/// apagado por defecto. Con eso prendido se reservan con sus <c>departureIds</c> en /api/reservations.</item>
/// </list>
/// Además devuelve los paquetes que vuelan la misma ruta.
/// </summary>
public class FlightSearchService(
    IFlightProvider flightProvider,
    TurisClickDbContext db,
    IOptions<FlightsOptions> options,
    ILogger<FlightSearchService> logger) : IFlightSearchService
{
    /// <summary>Techo de ofertas mostradas. El proveedor devuelve decenas; una pantalla de teléfono no.</summary>
    private const int MaxOptions = 8;

    public async Task<SearchFlightsResponse> SearchAsync(SearchFlightsRequest request, CancellationToken ct)
    {
        var origin = AirportCatalog.Normalize(request.OriginIata);
        var destination = AirportCatalog.Normalize(request.DestinationIata);
        var cabin = Enum.Parse<FlightCabinClass>(request.CabinClass, ignoreCase: true);

        var response = new SearchFlightsResponse
        {
            OriginIata = origin,
            OriginLabel = AirportCatalog.Describe(origin),
            DestinationIata = destination,
            DestinationLabel = AirportCatalog.Describe(destination),
            DepartureDate = request.DepartureDate,
            ReturnDate = request.ReturnDate,
            Passengers = request.TotalPassengers,
            Adults = request.EffectiveAdults,
            ChildrenAges = [.. request.ChildrenAges],
            Infants = request.Infants,
            CabinClass = cabin.ToString(),
            InventoryMode = InventoryModeFor(flightProvider, offers: []),
            Bookable = false,
            // Sin ofertas también se rotula: un "no hay vuelos" simulado no es un "no hay vuelos" real.
            TestMode = InventoryModeFor(flightProvider, offers: []) != FlightInventoryModes.Live,
            Packages = await RoutePackagesAsync(origin, destination, ct),
        };

        // Vuelos de operador (Oleada 18), sólo si siguen prendidos: con el metabuscador no se ofrecen.
        List<FlightOptionResponse> providerOptions = options.Value.ProviderFlights.Enabled
            ? await ProviderFlightOptionsAsync(origin, destination, cabin, request, ct)
            : [];
        response.Options.AddRange(providerOptions);
        response.Bookable = providerOptions.Count > 0;

        var slices = new List<FlightSliceRequest> { new(origin, destination, request.DepartureDate) };
        if (request.ReturnDate is { } back)
            slices.Add(new FlightSliceRequest(destination, origin, back));

        FlightSearchResult? search = null;
        string? airlineNotice = null;
        try
        {
            search = await flightProvider.SearchAsync(
                new FlightSearchRequest(
                    slices, request.EffectiveAdults, cabin, MaxOptions, request.ChildrenAges, request.Infants), ct);
        }
        catch (FlightProviderException ex)
        {
            // Que el proveedor falle no es un error de la app: se dice y se deja lo demás en pie, igual que
            // en la ficha de un paquete.
            logger.LogWarning(ex, "Falló la búsqueda de vuelos {Origin}→{Destination}.", origin, destination);
            airlineNotice = "No pudimos consultar vuelos en este momento. Probá de nuevo en unos minutos.";
        }

        var simulated = flightProvider is Providers.FakeFlightProvider;
        var redirectHosts = options.Value.RedirectHostsFor(options.Value.Provider);
        foreach (var offer in (search?.Offers ?? []).OrderBy(o => o.Price.Amount).Take(MaxOptions))
        {
            response.Options.Add(new FlightOptionResponse
            {
                Price = new MoneyResponse { Amount = offer.Price.Amount, Currency = offer.Price.Currency },
                CarrierName = offer.OwnerName,
                CarrierIata = offer.OwnerIataCode,
                Slices = FlightResponseMapper.ToSlices(offer),
                Source = FlightOfferSources.Airline,
                Bookable = false,
                Redirect = FlightRedirectPolicy.Evaluate(offer, simulated, redirectHosts),
            });

            response.TestMode |= !offer.LiveMode;
        }

        if (search is not null) response.InventoryMode = InventoryModeFor(flightProvider, search.Offers);

        // Sin nada que mostrar se explica por qué, y "el proveedor falló" no se disfraza de "no hay vuelos".
        if (response.Options.Count == 0)
            response.Notice = airlineNotice
                ?? $"No encontramos vuelos para el {request.DepartureDate:dd/MM} entre {AirportCatalog.Describe(origin)} y {AirportCatalog.Describe(destination)}.";

        logger.LogInformation(
            "Búsqueda de vuelos {Origin}→{Destination} el {Date}: {Provider} de operador, {Airline} de aerolínea.",
            origin, destination, request.DepartureDate, providerOptions.Count, response.Options.Count - providerOptions.Count);

        return response;
    }

    /// <summary>Techo de opciones de operador: con ida y vuelta, las combinaciones crecen rápido.</summary>
    private const int MaxProviderOptions = 10;

    /// <summary>
    /// Vuelos de operador para la búsqueda: publicados, no suspendidos, de una empresa aprobada y autorizada,
    /// en la ruta, la fecha y la cabina pedidas, con asientos para todos los que viajan sentados. En ida y vuelta,
    /// pares del mismo operador y en la misma moneda, con la vuelta saliendo después de que llegue la ida.
    ///
    /// La duración no se informa: los horarios son locales de cada aeropuerto y el sistema no conoce sus husos,
    /// así que una resta daría una duración falsa en cuanto el vuelo cruce de huso.
    /// </summary>
    private async Task<List<FlightOptionResponse>> ProviderFlightOptionsAsync(
        string origin, string destination, FlightCabinClass cabin, SearchFlightsRequest request, CancellationToken ct)
    {
        var adults = request.EffectiveAdults;
        var children = request.ChildrenAges.Count;
        var infants = request.Infants;
        var seats = adults + children;

        var outbound = await SellableDeparturesAsync(origin, destination, request.DepartureDate, cabin, seats, ct);
        if (outbound.Count == 0) return [];

        var inbound = request.ReturnDate is { } back
            ? await SellableDeparturesAsync(destination, origin, back, cabin, seats, ct)
            : null;

        decimal LegPrice(ProviderFlightDeparture d) => adults * d.AdultPrice + children * d.ChildPrice + infants * d.InfantPrice;

        var combinations = inbound is null
            ? outbound.Select(d => new[] { d })
            : outbound.SelectMany(there => inbound
                .Where(home => home.ProviderFlight!.CompanyId == there.ProviderFlight!.CompanyId
                    && home.Currency == there.Currency
                    && home.DepartureAt > there.ArrivalAt)
                .Select(home => new[] { there, home }));

        return
        [
            .. combinations
                .Select(legs => new { Legs = legs, Price = legs.Sum(LegPrice) })
                .OrderBy(c => c.Price)
                .Take(MaxProviderOptions)
                .Select(c =>
                {
                    var first = c.Legs[0].ProviderFlight!;
                    return new FlightOptionResponse
                    {
                        Price = new MoneyResponse { Amount = c.Price, Currency = c.Legs[0].Currency },
                        CarrierName = first.CarrierName,
                        CarrierIata = first.CarrierIata,
                        Slices = [.. c.Legs.Select(ToSlice)],
                        Source = FlightOfferSources.Provider,
                        Bookable = true,
                        ProviderFlight = new ProviderFlightOfferResponse
                        {
                            DepartureIds = [.. c.Legs.Select(l => l.Id)],
                            OperatorCompanyId = first.CompanyId,
                            OperatorName = first.Company!.Name,
                            SeatsLeft = c.Legs.Min(l => l.AvailableSeats),
                            CancellationPolicy = CancellationPolicyRules.Deserialize(first.CancellationPolicy),
                            FareRules = first.FareRules,
                            Demo = true,
                        },
                    };
                }),
        ];
    }

    private Task<List<ProviderFlightDeparture>> SellableDeparturesAsync(
        string origin, string destination, DateOnly date, FlightCabinClass cabin, int seats, CancellationToken ct)
    {
        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        // La columna es timestamp SIN huso (hora local del aeropuerto): el parámetro tiene que ir sin Kind, o
        // Npgsql lo rechaza. Comparar con la hora UTC es una aproximación prudente (ver EnsureFlightIsBookable).
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        return db.ProviderFlightDepartures
            .AsNoTracking()
            .Include(d => d.ProviderFlight).ThenInclude(f => f!.Company)
            .Where(d => d.Status == ProviderFlightDepartureStatus.OPEN
                && d.DepartureAt >= dayStart && d.DepartureAt < dayEnd && d.DepartureAt > now
                && d.TotalSeats - d.ReservedSeats >= seats
                && d.ProviderFlight!.OriginIata == origin
                && d.ProviderFlight.DestinationIata == destination
                && d.ProviderFlight.CabinClass == cabin
                && d.ProviderFlight.Status == ProviderFlightStatus.PUBLISHED
                && d.ProviderFlight.SuspendedAt == null
                && d.ProviderFlight.Company!.Status == CompanyStatus.APPROVED
                && d.ProviderFlight.Company.CanSellFlights)
            .ToListAsync(ct);
    }

    private static FlightSliceResponse ToSlice(ProviderFlightDeparture departure)
    {
        var flight = departure.ProviderFlight!;
        return new FlightSliceResponse
        {
            OriginIata = flight.OriginIata,
            DestinationIata = flight.DestinationIata,
            DurationMinutes = null,
            Stops = 0,
            Segments =
            [
                new FlightSegmentResponse
                {
                    OriginIata = flight.OriginIata,
                    DestinationIata = flight.DestinationIata,
                    DepartingAt = departure.DepartureAt,
                    ArrivingAt = departure.ArrivalAt,
                    CarrierIata = flight.CarrierIata,
                    CarrierName = flight.CarrierName,
                    FlightNumber = flight.FlightNumber,
                    CheckedBags = flight.CheckedBags,
                },
            ],
        };
    }

    /// <summary>
    /// Cómo rotular las ofertas. El proveedor falso inventa los vuelos: eso es SIMULATED, y la app lo dice
    /// con todas las letras. Un proveedor real en modo de prueba (Duffel con token de test) devuelve vuelos
    /// con forma real que no se pueden emitir: SANDBOX. Sólo una oferta marcada live por el proveedor es LIVE.
    /// </summary>
    internal static string InventoryModeFor(IFlightProvider provider, IReadOnlyCollection<FlightOffer> offers)
    {
        if (provider is Providers.FakeFlightProvider) return FlightInventoryModes.Simulated;
        return offers.Count > 0 && offers.All(o => o.LiveMode) ? FlightInventoryModes.Live : FlightInventoryModes.Sandbox;
    }

    /// <summary>
    /// Los paquetes publicados que vuelan esta ruta: mismo aeropuerto de destino y el origen entre los que
    /// el operador ofrece. Se filtra en memoria porque los orígenes viven serializados en una columna de
    /// texto (mismo criterio que el resto del módulo) y son un puñado de filas.
    ///
    /// Una empresa suspendida (UC-A-08) no ofrece nada: sus paquetes conservan su propio estado para que
    /// la reactivación los devuelva, así que el filtro va acá, igual que en el catálogo público.
    /// </summary>
    private async Task<List<FlightRoutePackageResponse>> RoutePackagesAsync(
        string origin, string destination, CancellationToken ct)
    {
        var candidates = await db.Packages
            .AsNoTracking()
            .Include(p => p.FlightRule)
            .Include(p => p.Images)
            .Include(p => p.Destination)
            .Where(p => p.Status == PublicationStatus.PUBLISHED
                        && p.Company!.Status != CompanyStatus.SUSPENDED
                        && p.IncludesFlight
                        && p.FlightRule != null
                        && p.FlightRule.DestinationIata == destination)
            .ToListAsync(ct);

        return
        [
            .. candidates
                .Where(p => p.FlightRule!.Origins().Contains(origin))
                .OrderBy(p => p.Price)
                .Select(p => new FlightRoutePackageResponse
                {
                    Id = p.Id,
                    Title = p.Title,
                    DestinationName = p.Destination?.Name ?? string.Empty,
                    DurationDays = p.DurationDays,
                    Price = new MoneyResponse { Amount = p.Price, Currency = p.Currency },
                    CoverImageUrl = p.Images.FirstOrDefault(i => i.IsCover)?.Url ?? p.Images.FirstOrDefault()?.Url,
                }),
        ];
    }
}

/// <summary>
/// Mapeo de una oferta del proveedor a la forma que viaja al cliente. Vive acá y no dentro de un servicio
/// porque lo usan dos caminos distintos —la cotización de un paquete y la búsqueda suelta— y la app dibuja
/// los dos con el mismo componente: si se separan, se separan también las pantallas.
/// </summary>
public static class FlightResponseMapper
{
    public static List<FlightSliceResponse> ToSlices(FlightOffer offer) =>
    [
        .. offer.Slices.Select(slice => new FlightSliceResponse
        {
            OriginIata = slice.OriginIata,
            DestinationIata = slice.DestinationIata,
            DurationMinutes = slice.Duration is { } duration ? (int)duration.TotalMinutes : null,
            Stops = Math.Max(slice.Segments.Count - 1, 0),
            Segments =
            [
                .. slice.Segments.Select(segment => new FlightSegmentResponse
                {
                    OriginIata = segment.OriginIata,
                    DestinationIata = segment.DestinationIata,
                    DepartingAt = segment.DepartingAt,
                    ArrivingAt = segment.ArrivingAt,
                    CarrierIata = segment.MarketingCarrierIata,
                    CarrierName = segment.MarketingCarrierName,
                    FlightNumber = segment.FlightNumber,
                    CheckedBags = segment.CheckedBags,
                }),
            ],
        }),
    ];
}
