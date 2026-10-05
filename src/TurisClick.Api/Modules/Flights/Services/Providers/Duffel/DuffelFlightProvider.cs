using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace TurisClick.Api.Modules.Flights.Services.Providers.Duffel;

/// <summary>
/// Adapter de la Flights API de Duffel (https://duffel.com/docs/api).
///
/// Flujo real, verificado en la documentación oficial y contra el entorno de prueba:
///   POST /air/offer_requests?return_offers=true   -> buscar
///   GET  /air/offers/{id}                         -> revalidar (puede devolver otro total_amount)
///   POST /air/orders                              -> reservar
///   GET  /air/orders/{id}                         -> consultar (reconciliación)
///
/// Dos reglas que este adapter hace cumplir y conviene no perder de vista:
///
/// 1. **El precio que se manda al crear la orden es el que devolvió la revalidación**, nunca uno que
///    haya pasado por el cliente. Duffel rechaza la orden si no coincide con su precio vigente, lo que
///    convierte un intento de manipulación en un error, no en una venta mal cobrada.
/// 2. **El token nunca se escribe en un log.** Las excepciones llevan el código y el mensaje de Duffel,
///    jamás la cabecera de autorización.
/// </summary>
public partial class DuffelFlightProvider : IFlightProvider
{
    private readonly HttpClient _http;
    private readonly DuffelOptions _options;
    private readonly ILogger<DuffelFlightProvider> _logger;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => "Duffel";

    public DuffelFlightProvider(HttpClient http, IOptions<FlightsOptions> options, ILogger<DuffelFlightProvider> logger)
    {
        _options = options.Value.Duffel;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.AccessToken))
            throw new InvalidOperationException(
                "Falta Flights:Duffel:AccessToken. En desarrollo va en User Secrets; nunca en appsettings ni en el repositorio.");

        // Cinturón de seguridad: con el proveedor real configurado, un token que no sea de prueba
        // significaría reservas con dinero de verdad. Se corta en el arranque, no en la primera venta.
        if (_options.RequireTestToken && !_options.IsTestToken)
            throw new InvalidOperationException(
                "El token de Duffel no es de prueba (no empieza con 'duffel_test_'). Para usar uno real hay que " +
                "poner Flights:Duffel:RequireTestToken en false de forma explícita.");

        _http = http;
        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        _http.DefaultRequestHeaders.Add("Duffel-Version", _options.ApiVersion);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<FlightSearchResult> SearchAsync(FlightSearchRequest request, CancellationToken ct)
    {
        var body = new DuffelEnvelope<DuffelOfferRequestBody>(new DuffelOfferRequestBody(
            [.. request.Slices.Select(s => new DuffelSliceRequest(
                s.OriginIata, s.DestinationIata, s.DepartureDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))],
            [.. Enumerable.Range(0, request.Adults).Select(_ => new DuffelPassengerRequest("adult"))],
            CabinClassValue(request.CabinClass)));

        var stopwatch = Stopwatch.StartNew();
        var offerRequest = await SendAsync<DuffelOfferRequest>(
            HttpMethod.Post, "air/offer_requests?return_offers=true", body, ct);
        stopwatch.Stop();

        var offers = (offerRequest.Offers ?? [])
            .Take(request.MaxOffers)
            .Select(MapOffer)
            .ToList();

        _logger.LogInformation(
            "Duffel search {OfferRequestId}: {Count} oferta(s) en {Elapsed} ms.",
            offerRequest.Id, offers.Count, stopwatch.ElapsedMilliseconds);

        return new FlightSearchResult(offerRequest.Id, offers, stopwatch.Elapsed);
    }

    public async Task<FlightOffer> RefreshOfferAsync(string offerId, CancellationToken ct)
    {
        // Duffel lo dice sin vueltas: los precios de una búsqueda no están garantizados al reservar, y
        // este GET puede devolver un total distinto. Por eso se llama SIEMPRE antes de crear la orden.
        var offer = await SendAsync<DuffelOffer>(HttpMethod.Get, $"air/offers/{offerId}", null, ct);
        return MapOffer(offer);
    }

    public async Task<FlightOrderResult> CreateOrderAsync(FlightOrderRequest request, CancellationToken ct)
    {
        var body = new DuffelEnvelope<DuffelCreateOrderBody>(new DuffelCreateOrderBody(
            "instant",
            [request.OfferId],
            // En modo de prueba el saldo es ilimitado y el pago se declara contra el balance de la
            // cuenta: no interviene ninguna tarjeta ni ningún importe real.
            [new DuffelPayment(
                "balance",
                request.ConfirmedPrice.Currency,
                request.ConfirmedPrice.Amount.ToString("0.00", CultureInfo.InvariantCulture))],
            [.. request.Passengers.Select(p => new DuffelOrderPassenger(
                p.ProviderPassengerId,
                p.GivenName,
                p.FamilyName,
                p.BornOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                p.Gender,
                p.Title,
                p.Email,
                p.PhoneNumber))]));

        var order = await SendAsync<DuffelOrder>(HttpMethod.Post, "air/orders", body, ct);

        _logger.LogInformation(
            "Duffel order {OrderId} creada (localizador {Reference}, live_mode={LiveMode}).",
            order.Id, order.BookingReference, order.LiveMode);

        return MapOrder(order);
    }

    public async Task<FlightOrderResult?> GetOrderAsync(string orderId, CancellationToken ct)
    {
        try
        {
            var order = await SendAsync<DuffelOrder>(HttpMethod.Get, $"air/orders/{orderId}", null, ct);
            return MapOrder(order);
        }
        catch (FlightProviderRequestException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Cancelación en dos pasos, como la define Duffel: primero se crea una cancelación pendiente —que
    /// informa cuánto se reintegra— y después se confirma. Son dos llamadas porque la persona tiene que
    /// poder ver el reembolso antes de aceptarlo.
    /// </summary>
    public async Task<FlightCancellationResult> CancelOrderAsync(string orderId, bool confirm, CancellationToken ct)
    {
        var pending = await SendAsync<DuffelOrderCancellation>(
            HttpMethod.Post, "air/order_cancellations", new DuffelEnvelope<DuffelCancellationBody>(new DuffelCancellationBody(orderId)), ct);

        if (!confirm) return MapCancellation(pending);

        var confirmed = await SendAsync<DuffelOrderCancellation>(
            HttpMethod.Post, $"air/order_cancellations/{pending.Id}/actions/confirm", null, ct);

        return MapCancellation(confirmed);
    }

    // ---------------------------------------------------------------- transporte

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(method, path);
        if (body is not null) message.Content = JsonContent.Create(body, options: Json);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Timeout propio del HttpClient: para el llamador es lo mismo que el proveedor caído.
            throw new FlightProviderUnavailableException($"Duffel no respondió en {_options.TimeoutSeconds} s.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new FlightProviderUnavailableException("No se pudo contactar a Duffel.", ex);
        }

        var payload = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode) throw TranslateError(response, payload);

        try
        {
            var envelope = JsonSerializer.Deserialize<DuffelEnvelope<T>>(payload, Json);
            return envelope!.Data ?? throw new FlightProviderResponseException("Duffel devolvió una respuesta sin datos.");
        }
        catch (JsonException ex)
        {
            throw new FlightProviderResponseException("No se pudo interpretar la respuesta de Duffel.", ex);
        }
    }

    /// <summary>
    /// Traduce el error del proveedor a la decisión que el llamador tiene que tomar. El cuerpo de Duffel
    /// trae `errors[].code`, que es lo que distingue una oferta vencida de un dato inválido.
    /// </summary>
    private static FlightProviderException TranslateError(HttpResponseMessage response, string payload)
    {
        DuffelError? first = null;
        try
        {
            first = JsonSerializer.Deserialize<DuffelErrorEnvelope>(payload, Json)?.Errors?.FirstOrDefault();
        }
        catch (JsonException)
        {
            // Un 5xx puede venir en HTML: no es motivo para perder el código de estado.
        }

        var status = (int)response.StatusCode;
        var detail = first?.Message ?? first?.Title ?? $"Duffel respondió {status}.";
        var code = first?.Code;

        if (status is 401 or 403) return new FlightProviderAuthException("Duffel rechazó las credenciales.");

        if (status == 429)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            return new FlightProviderRateLimitException("Se superó el límite de llamadas a Duffel.", retryAfter);
        }

        if (status >= 500) return new FlightProviderUnavailableException($"Duffel respondió {status}: {detail}");

        if (code is not null && ExpiredOfferCodes.Contains(code))
            return new FlightOfferExpiredException($"La oferta ya no está disponible: {detail}");

        return new FlightProviderRequestException(detail, status, code);
    }

    private static readonly HashSet<string> ExpiredOfferCodes =
    [
        "offer_no_longer_available",
        "offer_expired",
        "offer_request_not_found",
    ];

    // ---------------------------------------------------------------- mapeo

    internal static FlightOffer MapOffer(DuffelOffer offer) => new(
        offer.Id,
        new FlightPrice(ParseAmount(offer.TotalAmount), offer.TotalCurrency ?? string.Empty),
        [.. (offer.Slices ?? []).Select(MapSlice)],
        offer.ExpiresAt,
        offer.IdentityDocumentsRequired,
        offer.PaymentRequirements?.RequiresInstantPayment ?? true,
        offer.LiveMode,
        offer.Owner?.Name,
        offer.Owner?.IataCode,
        [.. (offer.Passengers ?? []).Select(p => new FlightPassengerSlot(p.Id, p.Type ?? "adult"))]);

    internal static FlightPassengerRequirements Requirements(FlightOffer offer) =>
        new(offer.IdentityDocumentsRequired, offer.Passengers);

    private static FlightSlice MapSlice(DuffelSlice slice) => new(
        slice.Origin?.IataCode ?? string.Empty,
        slice.Destination?.IataCode ?? string.Empty,
        ParseDuration(slice.Duration),
        [.. (slice.Segments ?? []).Select(MapSegment)]);

    private static FlightSegment MapSegment(DuffelSegment segment) => new(
        segment.Origin?.IataCode ?? string.Empty,
        segment.Destination?.IataCode ?? string.Empty,
        segment.DepartingAt ?? default,
        segment.ArrivingAt ?? default,
        segment.MarketingCarrier?.IataCode,
        segment.MarketingCarrier?.Name,
        segment.FlightNumber,
        segment.Aircraft?.Name,
        // El equipaje viene por pasajero y por tipo; si el proveedor no lo informa queda nulo y la
        // interfaz dice "sin datos" en vez de inventar una franquicia.
        segment.Passengers?
            .SelectMany(p => p.Baggages ?? [])
            .Where(b => b.Type == "checked")
            .Select(b => (int?)b.Quantity)
            .FirstOrDefault());

    private static FlightOrderResult MapOrder(DuffelOrder order) => new(
        order.Id,
        order.BookingReference,
        new FlightPrice(ParseAmount(order.TotalAmount), order.TotalCurrency ?? string.Empty),
        order.LiveMode,
        order.CreatedAt,
        [.. (order.Slices ?? []).Select(MapSlice)]);

    private static FlightCancellationResult MapCancellation(DuffelOrderCancellation cancellation) => new(
        cancellation.Id,
        string.IsNullOrWhiteSpace(cancellation.RefundAmount) ? null : ParseAmount(cancellation.RefundAmount),
        cancellation.RefundCurrency,
        cancellation.RefundTo,
        cancellation.ConfirmedAt);

    /// <summary>Duffel publica los importes como string. Se parsea en cultura invariante, una sola vez.</summary>
    private static decimal ParseAmount(string? amount) =>
        decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : 0m;

    /// <summary>Duración en ISO 8601 ("PT5H30M"). Se traduce acá para que el dominio maneje un TimeSpan.</summary>
    private static TimeSpan? ParseDuration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration)) return null;

        var match = IsoDurationRegex().Match(duration);
        if (!match.Success) return null;

        var hours = match.Groups["h"].Success ? int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture) : 0;
        var minutes = match.Groups["m"].Success ? int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        return new TimeSpan(hours, minutes, 0);
    }

    private static string CabinClassValue(FlightCabinClass cabin) => cabin switch
    {
        FlightCabinClass.PREMIUM_ECONOMY => "premium_economy",
        FlightCabinClass.BUSINESS => "business",
        FlightCabinClass.FIRST => "first",
        _ => "economy",
    };

    [GeneratedRegex(@"^P(?:\d+D)?T(?:(?<h>\d+)H)?(?:(?<m>\d+)M)?")]
    private static partial Regex IsoDurationRegex();
}
