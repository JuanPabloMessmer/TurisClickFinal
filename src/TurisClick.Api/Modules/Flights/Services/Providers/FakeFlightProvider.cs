using System.Globalization;

namespace TurisClick.Api.Modules.Flights.Services.Providers;

/// <summary>
/// Proveedor aéreo determinístico: el que corre en tests, en Azure y en cualquier demo sin red. No es un
/// mock pobre — reproduce los casos difíciles del inventario aéreo, que son los que importan: oferta
/// vencida, precio que cambia al revalidar y oferta que desaparece.
///
/// Se dispara por ruta, igual que hace Duffel en su entorno de prueba, así que un test puede pedir el
/// caso que quiere ejercitar sin tocar la red:
///   XXX -> cualquier destino  : sin ofertas
///   EXP -> cualquier destino  : la oferta nace vencida
///   CHG -> cualquier destino  : al revalidar, el precio sube
///   GON -> cualquier destino  : al revalidar, la oferta ya no existe
/// </summary>
public class FakeFlightProvider(TimeProvider? timeProvider = null) : IFlightProvider
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public string Name => "Fake";

    public const string NoOffersOrigin = "XXX";
    public const string ExpiredOrigin = "EXP";
    public const string PriceChangeOrigin = "CHG";
    public const string GoneOrigin = "GON";

    public Task<FlightSearchResult> SearchAsync(FlightSearchRequest request, CancellationToken ct)
    {
        var first = request.Slices[0];
        var searchId = $"orq_fake_{first.OriginIata}{first.DestinationIata}{first.DepartureDate:yyyyMMdd}";

        if (first.OriginIata == NoOffersOrigin)
            return Task.FromResult(new FlightSearchResult(searchId, [], TimeSpan.Zero));

        var offers = Enumerable.Range(0, 2)
            .Select(index => BuildOffer(request, index))
            .ToList();

        return Task.FromResult(new FlightSearchResult(searchId, offers, TimeSpan.FromMilliseconds(12)));
    }

    public Task<FlightOffer> RefreshOfferAsync(string offerId, CancellationToken ct)
    {
        if (offerId.Contains(GoneOrigin, StringComparison.Ordinal))
            throw new FlightOfferExpiredException("La oferta ya no está disponible.");

        var request = DecodeOffer(offerId);
        var offer = BuildOffer(request.Request, request.Index);

        // El caso que más tiene que ejercitarse: revalidar devuelve otro precio y el flujo no puede
        // cobrar el viejo.
        if (request.Request.Slices[0].OriginIata == PriceChangeOrigin)
            offer = offer with { Price = new FlightPrice(offer.Price.Amount + 75m, offer.Price.Currency) };

        return Task.FromResult(offer);
    }

    public Task<FlightOrderResult> CreateOrderAsync(FlightOrderRequest request, CancellationToken ct)
    {
        if (request.OfferId.Contains(GoneOrigin, StringComparison.Ordinal))
            throw new FlightOfferExpiredException("La oferta ya no está disponible.");

        var decoded = DecodeOffer(request.OfferId);
        var slices = BuildOffer(decoded.Request, decoded.Index).Slices;

        return Task.FromResult(new FlightOrderResult(
            $"ord_fake_{decoded.Request.Slices[0].OriginIata}{decoded.Index}",
            $"FAKE{decoded.Index}{decoded.Request.Slices[0].DestinationIata}",
            request.ConfirmedPrice,
            LiveMode: false,
            _time.GetUtcNow(),
            slices));
    }

    public Task<FlightOrderResult?> GetOrderAsync(string orderId, CancellationToken ct) =>
        Task.FromResult<FlightOrderResult?>(null);

    // ---------------------------------------------------------------- construcción

    private FlightOffer BuildOffer(FlightSearchRequest request, int index)
    {
        var slice = request.Slices[0];
        var departure = slice.DepartureDate.ToDateTime(new TimeOnly(8 + index * 4, 15), DateTimeKind.Unspecified);
        var arrival = departure.AddHours(1).AddMinutes(25);
        var expired = slice.OriginIata == ExpiredOrigin;

        var price = 120m + index * 35m;

        return new FlightOffer(
            EncodeOffer(request, index),
            new FlightPrice(price * request.Adults, "USD"),
            [
                new FlightSlice(
                    slice.OriginIata,
                    slice.DestinationIata,
                    TimeSpan.FromMinutes(85),
                    [
                        new FlightSegment(
                            slice.OriginIata,
                            slice.DestinationIata,
                            departure,
                            arrival,
                            "ZZ",
                            "Fake Airways",
                            $"{100 + index}",
                            "Airbus A320",
                            CheckedBags: index == 0 ? 1 : 0),
                    ]),
            ],
            expired ? _time.GetUtcNow().AddMinutes(-1) : _time.GetUtcNow().AddMinutes(20),
            IdentityDocumentsRequired: false,
            InstantPaymentRequired: true,
            LiveMode: false,
            "Fake Airways",
            "ZZ",
            [.. Enumerable.Range(0, request.Adults).Select(i => new FlightPassengerSlot($"pas_fake_{i}", "adult"))]);
    }

    /// <summary>
    /// El id de oferta lleva adentro lo necesario para reconstruirla: el proveedor falso no guarda estado
    /// y así dos instancias distintas (una en el test, otra en el servicio) se comportan igual.
    /// </summary>
    private static string EncodeOffer(FlightSearchRequest request, int index)
    {
        var slice = request.Slices[0];
        return string.Create(CultureInfo.InvariantCulture,
            $"off_fake_{slice.OriginIata}_{slice.DestinationIata}_{slice.DepartureDate:yyyyMMdd}_{request.Adults}_{index}");
    }

    private static (FlightSearchRequest Request, int Index) DecodeOffer(string offerId)
    {
        var parts = offerId.Split('_');
        if (parts.Length < 7) throw new FlightProviderRequestException("Id de oferta desconocido.", 404);

        var date = DateOnly.ParseExact(parts[4], "yyyyMMdd", CultureInfo.InvariantCulture);
        var adults = int.Parse(parts[5], CultureInfo.InvariantCulture);
        var index = int.Parse(parts[6], CultureInfo.InvariantCulture);

        return (new FlightSearchRequest([new FlightSliceRequest(parts[2], parts[3], date)], adults), index);
    }
}
