using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Companies.Entities;
using TurisClick.Api.Modules.Flights.Dtos;
using TurisClick.Api.Modules.Packages.Entities;
using TurisClick.Api.Modules.Experiences.Entities;

namespace TurisClick.Api.Modules.Flights.Services;

public interface IFlightSearchService
{
    Task<SearchFlightsResponse> SearchAsync(SearchFlightsRequest request, CancellationToken ct);
}

/// <summary>
/// Búsqueda de vuelos sin paquete: el explorador de la app.
///
/// La diferencia con <see cref="PackageFlightService"/> no es técnica, es de dominio. Cotizar el vuelo de un
/// paquete **escribe** filas (`flight_quotes`) porque esa cotización se va a reservar y hay que poder
/// revalidarla, atribuirla a una persona y cobrarla. Buscar vuelos sueltos **no escribe nada**: en TurisClick
/// un pasaje no se vende solo —la reserva aérea cuelga de una reserva de paquete— así que una búsqueda
/// suelta que devolviera un identificador reservable estaría prometiendo algo que el sistema no hace.
///
/// Por eso la respuesta no trae ningún id de cotización, y por eso incluye los paquetes que vuelan esa misma
/// ruta: es el único camino real desde "qué vuelos hay" hasta "esto lo puedo comprar".
/// </summary>
public class FlightSearchService(
    IFlightProvider flightProvider,
    TurisClickDbContext db,
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
            Passengers = request.Passengers,
            CabinClass = cabin.ToString(),
            Packages = await RoutePackagesAsync(origin, destination, ct),
        };

        var slices = new List<FlightSliceRequest> { new(origin, destination, request.DepartureDate) };
        if (request.ReturnDate is { } back)
            slices.Add(new FlightSliceRequest(destination, origin, back));

        FlightSearchResult search;
        try
        {
            search = await flightProvider.SearchAsync(
                new FlightSearchRequest(slices, request.Passengers, cabin, MaxOptions), ct);
        }
        catch (FlightProviderException ex)
        {
            // Que el proveedor falle no es un error de la app: se dice y se deja lo demás en pie, igual que
            // en la ficha de un paquete.
            logger.LogWarning(ex, "Falló la búsqueda de vuelos {Origin}→{Destination}.", origin, destination);
            response.Notice = "No pudimos consultar vuelos en este momento. Probá de nuevo en unos minutos.";
            return response;
        }

        if (search.Offers.Count == 0)
        {
            response.Notice =
                $"No encontramos vuelos para el {request.DepartureDate:dd/MM} entre {AirportCatalog.Describe(origin)} y {AirportCatalog.Describe(destination)}.";
            return response;
        }

        foreach (var offer in search.Offers.OrderBy(o => o.Price.Amount).Take(MaxOptions))
        {
            response.Options.Add(new FlightOptionResponse
            {
                Price = new MoneyResponse { Amount = offer.Price.Amount, Currency = offer.Price.Currency },
                CarrierName = offer.OwnerName,
                CarrierIata = offer.OwnerIataCode,
                Slices = FlightResponseMapper.ToSlices(offer),
            });

            response.TestMode |= !offer.LiveMode;
        }

        logger.LogInformation(
            "Búsqueda de vuelos {Origin}→{Destination} el {Date}: {Count} oferta(s).",
            origin, destination, request.DepartureDate, response.Options.Count);

        return response;
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
