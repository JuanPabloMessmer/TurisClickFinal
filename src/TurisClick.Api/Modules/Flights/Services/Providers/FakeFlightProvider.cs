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
///   FAI -> cualquier destino  : al reservar, el proveedor rechaza los datos (falla definitiva)
///   UNK -> cualquier destino  : al reservar se pierde la respuesta, y la orden SÍ quedó creada
///   UNL -> cualquier destino  : al reservar se pierde la respuesta, y NO quedó ninguna orden
///   NET -> cualquier destino  : no se pudo abrir la conexión; la orden nunca salió
///   DOC -> cualquier destino  : la oferta exige documento de identidad
///   NRF -> cualquier destino  : al cancelar, la aerolínea no devuelve nada
///   NOC -> cualquier destino  : la aerolínea rechaza confirmar la cancelación
///   CNX -> cualquier destino  : consultar la orden la muestra YA cancelada
///   NIR -> cualquier destino  : al cancelar, la aerolínea NO informa cuánto devuelve
///
/// UNK y UNL son el par que importa: desde el lado del backend las dos fallas son idénticas —una
/// respuesta que no llegó—, y lo único que las distingue es lo que el proveedor contesta después. Es
/// exactamente la ambigüedad que la reconciliación tiene que resolver.
/// </summary>
public class FakeFlightProvider(TimeProvider? timeProvider = null) : IFlightProvider
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public string Name => "Fake";

    public const string NoOffersOrigin = "XXX";
    public const string ExpiredOrigin = "EXP";
    public const string PriceChangeOrigin = "CHG";
    public const string GoneOrigin = "GON";
    public const string OrderRejectedOrigin = "FAI";
    public const string UnknownButOrderedOrigin = "UNK";
    public const string UnknownAndLostOrigin = "UNL";
    public const string UnreachableOrigin = "NET";
    public const string DocumentsRequiredOrigin = "DOC";
    public const string NonRefundableOrigin = "NRF";
    public const string CancellationFailsOrigin = "NOC";
    public const string AlreadyCancelledOrigin = "CNX";
    public const string RefundUnknownOrigin = "NIR";

    public Task<FlightSearchResult> SearchAsync(FlightSearchRequest request, CancellationToken ct)
    {
        var first = request.Slices[0];
        var searchId = $"orq_fake_{first.OriginIata}{first.DestinationIata}{first.DepartureDate:yyyyMMdd}";

        if (first.OriginIata == NoOffersOrigin)
            return Task.FromResult(new FlightSearchResult(searchId, [], TimeSpan.Zero));

        var offers = Enumerable.Range(0, Math.Min(OffersPerSearch, Math.Max(request.MaxOffers, 1)))
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
        var origin = decoded.Request.Slices[0].OriginIata;

        if (origin == OrderRejectedOrigin)
            throw new FlightProviderRequestException(
                "El proveedor rechazó los datos de los pasajeros.", 422, "invalid_passenger_data");

        if (origin == UnreachableOrigin)
            throw new FlightProviderUnavailableException(
                "No se pudo abrir la conexión con el proveedor.", requestMayHaveBeenSent: false);

        // La orden se creó o no —eso lo decide el origen— pero en los dos casos el llamador se queda sin
        // respuesta. Es el escenario que obliga a reconciliar antes de volver a intentar.
        if (origin is UnknownButOrderedOrigin or UnknownAndLostOrigin)
            throw new FlightProviderUnavailableException(
                "El proveedor no respondió a tiempo.", requestMayHaveBeenSent: true);

        return Task.FromResult(BuildOrder(request, decoded));
    }

    public Task<FlightOrderResult?> GetOrderAsync(string orderId, CancellationToken ct)
    {
        if (!orderId.StartsWith("ord_fake_", StringComparison.Ordinal))
            return Task.FromResult<FlightOrderResult?>(null);

        var origin = orderId.Split('_') is { Length: > 2 } parts ? parts[2] : string.Empty;

        return Task.FromResult<FlightOrderResult?>(new FlightOrderResult(
            orderId,
            $"FAKE{origin}",
            new FlightPrice(120m, "USD"),
            LiveMode: false,
            _time.GetUtcNow(),
            [],
            OfferId: null,
            CorrelationKey: null,
            // La orden se muestra cancelada sólo en el escenario que lo pide: es lo que permite probar que la
            // resolución PREGUNTA antes de reintentar una cancelación.
            CancelledAt: orderId.Contains(AlreadyCancelledOrigin, StringComparison.Ordinal) ? _time.GetUtcNow() : null));
    }

    /// <summary>
    /// Lo que la reconciliación le pregunta al proveedor: ¿quedó una orden con esta oferta? UNK contesta
    /// que sí (el timeout tapó una compra real) y UNL que no (nunca se creó nada).
    /// </summary>
    public Task<FlightOrderResult?> FindOrderByOfferAsync(string offerId, string? correlationKey, CancellationToken ct)
    {
        if (!offerId.Contains(UnknownButOrderedOrigin, StringComparison.Ordinal))
            return Task.FromResult<FlightOrderResult?>(null);

        var decoded = DecodeOffer(offerId);
        var offer = BuildOffer(decoded.Request, decoded.Index);

        return Task.FromResult<FlightOrderResult?>(BuildOrder(
            new FlightOrderRequest(offerId, offer.Price, [], correlationKey), decoded));
    }

    /// <summary>
    /// Presupuesto de cancelación. El origen del vuelo decide qué contesta la aerolínea, igual que en el
    /// resto del proveedor falso:
    ///   NRF -> no reembolsable (devuelve 0)
    ///   UNK -> no informa cuánto devuelve (nulo, que no es lo mismo que cero)
    ///   resto -> reembolso parcial con cargo de cancelación
    /// </summary>
    public Task<FlightCancellationResult> QuoteCancellationAsync(string orderId, CancellationToken ct)
    {
        var origin = OriginOf(orderId);

        var refund = RefundFor(origin);

        return Task.FromResult(new FlightCancellationResult(
            $"ore_fake_{orderId}",
            refund,
            refund is null ? null : "USD",
            refund is null ? null : "balance",
            ConfirmedAt: null,
            ExpiresAt: _time.GetUtcNow().AddMinutes(30)));
    }

    public Task<FlightCancellationResult> ConfirmCancellationAsync(string cancellationId, CancellationToken ct)
    {
        if (cancellationId.Contains(CancellationFailsOrigin, StringComparison.Ordinal))
            throw new FlightProviderRequestException(
                "La aerolínea no aceptó la cancelación.", 422, "cancellation_not_allowed");

        var refund = RefundFor(OriginOf(cancellationId));

        return Task.FromResult(new FlightCancellationResult(
            cancellationId,
            refund,
            refund is null ? null : "USD",
            refund is null ? null : "balance",
            ConfirmedAt: _time.GetUtcNow(),
            ExpiresAt: null));
    }

    /// <summary>
    /// Cuánto devuelve la aerolínea según el escenario. `null` **no es cero**: es "no informó", que es un caso
    /// distinto y el flujo lo trata distinto.
    /// </summary>
    private static decimal? RefundFor(string origin) => origin switch
    {
        NonRefundableOrigin => 0m,
        RefundUnknownOrigin => null,
        UnknownButOrderedOrigin => null,
        _ => 100m,
    };

    /// <summary>El origen va dentro del id de la orden ("ord_fake_VVI0"), así que se lee de ahí.</summary>
    private static string OriginOf(string id)
    {
        var parts = id.Split('_');
        var tail = parts.Length > 2 ? parts[^1] : string.Empty;
        return tail.Length >= 3 ? tail[..3] : tail;
    }

    private FlightOrderResult BuildOrder(FlightOrderRequest request, (FlightSearchRequest Request, int Index) decoded)
    {
        var origin = decoded.Request.Slices[0].OriginIata;

        return new FlightOrderResult(
            $"ord_fake_{origin}{decoded.Index}",
            $"FAKE{decoded.Index}{decoded.Request.Slices[0].DestinationIata}",
            request.ConfirmedPrice,
            LiveMode: false,
            _time.GetUtcNow(),
            BuildOffer(decoded.Request, decoded.Index).Slices,
            request.OfferId,
            request.CorrelationKey);
    }

    // ---------------------------------------------------------------- construcción

    /// <summary>Cuántas ofertas devuelve una búsqueda: suficientes para que ordenar y filtrar signifiquen algo.</summary>
    public const int OffersPerSearch = 4;

    /// <summary>Más allá de esta distancia, sólo la primera oferta es directa: las demás conectan en un hub.</summary>
    private const double LongHaulKm = 3500;

    /// <summary>Velocidad de crucero efectiva y tiempo fijo de rodaje, ascenso y descenso.</summary>
    private const double CruiseKmPerHour = 780;
    private const int TaxiAndClimbMinutes = 35;

    /// <summary>Duración de un tramo cuando no se conocen las coordenadas (códigos de escenario como CHG o NET).</summary>
    private const int UnknownLegMinutes = 85;

    private const int LayoverMinutes = 95;

    /// <summary>
    /// Arma una oferta determinística. Lo que importa no es que sea realista —es una simulación y la app lo
    /// dice—, sino que sea **coherente**: un tramo por tramo pedido, duraciones que crecen con la distancia,
    /// escalas en los vuelos largos, y un precio que depende de cabina y pasajeros. Con dos ofertas idénticas
    /// salvo el horario, ordenar y filtrar resultados no tendría nada que mostrar.
    ///
    /// Las ofertas 0 y 1 conservan los horarios y precios base de siempre (08:15 y 12:15; 120 y 155 USD por
    /// adulto en económica) para que el paquete con vuelo se cotice igual que antes en rutas cortas.
    /// </summary>
    private FlightOffer BuildOffer(FlightSearchRequest request, int index)
    {
        var first = request.Slices[0];
        var expired = first.OriginIata == ExpiredOrigin;
        var outboundKm = AirportCatalog.DistanceKm(first.OriginIata, first.DestinationIata);

        // La oferta 2 siempre hace escala; en los vuelos largos, todas menos la primera.
        var withStop = index == 2 || (outboundKm > LongHaulKm && index > 0);

        // Equipaje: las cabinas altas llevan dos valijas; en económica, la mitad de las ofertas incluye una.
        var bags = request.CabinClass is FlightCabinClass.BUSINESS or FlightCabinClass.FIRST
            ? 2
            : index is 0 or 2 ? 1 : 0;

        var slices = request.Slices
            .Select((leg, legIndex) => BuildSlice(leg, index, legIndex, withStop, bags))
            .ToList();

        return new FlightOffer(
            EncodeOffer(request, index),
            new FlightPrice(PriceFor(request, index, outboundKm, withStop), "USD"),
            slices,
            expired ? _time.GetUtcNow().AddMinutes(-1) : _time.GetUtcNow().AddMinutes(20),
            IdentityDocumentsRequired: first.OriginIata == DocumentsRequiredOrigin,
            InstantPaymentRequired: true,
            LiveMode: false,
            "Fake Airways",
            "ZZ",
            [
                .. Enumerable.Range(0, request.Adults).Select(i => new FlightPassengerSlot($"pas_fake_{i}", "adult")),
                .. request.Children.Select((_, i) => new FlightPassengerSlot($"pas_fake_child_{i}", "child")),
                .. Enumerable.Range(0, request.Infants).Select(i => new FlightPassengerSlot($"pas_fake_infant_{i}", "infant_without_seat")),
            ]);
    }

    private static FlightSlice BuildSlice(FlightSliceRequest leg, int index, int legIndex, bool withStop, int bags)
    {
        // 08:15, 12:15, 16:15 y 20:15: una de mañana, una de mediodía, una de tarde y una de noche, para que
        // el filtro por horario tenga de qué filtrar.
        var departure = leg.DepartureDate.ToDateTime(new TimeOnly(8 + index * 4, 15), DateTimeKind.Unspecified);
        var flightNumber = 100 + index + legIndex * 10;

        var hub = withStop ? HubFor(leg.OriginIata, leg.DestinationIata) : null;

        var segments = new List<FlightSegment>();
        if (hub is null)
        {
            segments.Add(Segment(leg.OriginIata, leg.DestinationIata, departure, $"{flightNumber}", bags));
        }
        else
        {
            var toHub = Segment(leg.OriginIata, hub, departure, $"{flightNumber}", bags);
            var fromHub = Segment(hub, leg.DestinationIata, toHub.ArrivingAt.AddMinutes(LayoverMinutes), $"{flightNumber + 500}", bags);
            segments.Add(toHub);
            segments.Add(fromHub);
        }

        return new FlightSlice(
            leg.OriginIata,
            leg.DestinationIata,
            segments[^1].ArrivingAt - segments[0].DepartingAt,
            segments);
    }

    private static FlightSegment Segment(string origin, string destination, DateTime departure, string flightNumber, int bags) =>
        new(
            origin,
            destination,
            departure,
            departure.AddMinutes(LegMinutes(origin, destination)),
            "ZZ",
            "Fake Airways",
            flightNumber,
            "Airbus A320",
            CheckedBags: bags);

    private static int LegMinutes(string origin, string destination)
    {
        if (AirportCatalog.DistanceKm(origin, destination) is not { } km) return UnknownLegMinutes;

        var minutes = TaxiAndClimbMinutes + km / CruiseKmPerHour * 60;
        return (int)(Math.Round(minutes / 5) * 5);
    }

    /// <summary>
    /// Dónde conecta un vuelo con escala: un hub boliviano en los tramos cortos y uno sudamericano en los
    /// largos, nunca uno de los dos extremos del viaje.
    /// </summary>
    private static string HubFor(string origin, string destination)
    {
        string[] hubs = AirportCatalog.DistanceKm(origin, destination) > LongHaulKm
            ? ["LIM", "BOG", "GRU"]
            : ["CBB", "VVI", "LPB"];

        return hubs.First(hub => hub != origin && hub != destination);
    }

    /// <summary>
    /// Precio total de la oferta. Base por adulto (120 + 35 por posición) más un componente por distancia en
    /// los vuelos largos; cabina, ida y vuelta, niños (75%) e infantes en brazos (10%) lo escalan. Las rutas
    /// cortas en económica, sólo ida y sólo adultos dan exactamente lo mismo que antes.
    /// </summary>
    private static decimal PriceFor(FlightSearchRequest request, int index, double? outboundKm, bool withStop)
    {
        var perAdult = 120m + index * 35m;

        if (outboundKm is > 1500)
            perAdult += Math.Round((decimal)(outboundKm.Value - 1500) * 0.06m);

        if (withStop && index > 0)
            perAdult = Math.Round(perAdult * 0.9m);

        perAdult *= request.CabinClass switch
        {
            FlightCabinClass.PREMIUM_ECONOMY => 1.6m,
            FlightCabinClass.BUSINESS => 3m,
            FlightCabinClass.FIRST => 4.5m,
            _ => 1m,
        };

        if (request.Slices.Count > 1) perAdult *= 1.8m;

        var total = perAdult * request.Adults
                    + perAdult * 0.75m * request.Children.Count
                    + perAdult * 0.1m * request.Infants;

        return Math.Round(total, 2);
    }

    /// <summary>
    /// El id de oferta lleva adentro lo necesario para reconstruirla: el proveedor falso no guarda estado
    /// y así dos instancias distintas (una en el test, otra en el servicio) se comportan igual.
    ///
    /// Forma: <c>off_fake_ORIG_DEST_yyyyMMdd_adultos_índice[_cabina_edades_infantes_vuelta]</c>. La parte
    /// opcional es lo que hace que revalidar una ida y vuelta, o una oferta en business, devuelva el mismo
    /// precio que la búsqueda: si se perdiera, el flujo de reserva vería un "cambio de precio" que nadie hizo.
    /// </summary>
    private static string EncodeOffer(FlightSearchRequest request, int index)
    {
        var slice = request.Slices[0];
        var cabin = request.CabinClass.ToString()[0];
        var children = request.Children.Count == 0 ? "x" : string.Join('-', request.Children);
        var back = request.Slices.Count > 1 ? request.Slices[1].DepartureDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture) : "x";

        return string.Create(CultureInfo.InvariantCulture,
            $"off_fake_{slice.OriginIata}_{slice.DestinationIata}_{slice.DepartureDate:yyyyMMdd}_{request.Adults}_{index}_{cabin}_{children}_{request.Infants}_{back}");
    }

    private static (FlightSearchRequest Request, int Index) DecodeOffer(string offerId)
    {
        var parts = offerId.Split('_');
        if (parts.Length < 7) throw new FlightProviderRequestException("Id de oferta desconocido.", 404);

        var origin = parts[2];
        var destination = parts[3];
        var date = DateOnly.ParseExact(parts[4], "yyyyMMdd", CultureInfo.InvariantCulture);
        var adults = int.Parse(parts[5], CultureInfo.InvariantCulture);
        var index = int.Parse(parts[6], CultureInfo.InvariantCulture);

        var slices = new List<FlightSliceRequest> { new(origin, destination, date) };
        var cabin = FlightCabinClass.ECONOMY;
        List<int> children = [];
        var infants = 0;

        // Los ids anteriores no traen la parte opcional: se leen como sólo ida, económica y sólo adultos.
        if (parts.Length >= 11)
        {
            cabin = parts[7] switch
            {
                "P" => FlightCabinClass.PREMIUM_ECONOMY,
                "B" => FlightCabinClass.BUSINESS,
                "F" => FlightCabinClass.FIRST,
                _ => FlightCabinClass.ECONOMY,
            };

            if (parts[8] != "x")
                children = [.. parts[8].Split('-').Select(age => int.Parse(age, CultureInfo.InvariantCulture))];

            infants = int.Parse(parts[9], CultureInfo.InvariantCulture);

            if (parts[10] != "x")
                slices.Add(new FlightSliceRequest(
                    destination, origin, DateOnly.ParseExact(parts[10], "yyyyMMdd", CultureInfo.InvariantCulture)));
        }

        return (new FlightSearchRequest(slices, adults, cabin, ChildAges: children, Infants: infants), index);
    }
}
