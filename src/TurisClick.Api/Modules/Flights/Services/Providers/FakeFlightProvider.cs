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
            IdentityDocumentsRequired: slice.OriginIata == DocumentsRequiredOrigin,
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
