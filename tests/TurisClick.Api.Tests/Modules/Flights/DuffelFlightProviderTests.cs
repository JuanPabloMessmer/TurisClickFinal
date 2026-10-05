using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using TurisClick.Api.Modules.Flights.Services;
using TurisClick.Api.Modules.Flights.Services.Providers.Duffel;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Flights;

/// <summary>
/// Adapter de Duffel con un HttpMessageHandler simulado: **nunca** pega contra la API real. Las fixtures
/// de tests/fixtures/duffel reproducen la forma verificada contra el entorno de prueba (ver
/// docs/duffel-test-results.md), así que estos tests prueban el mapeo sin depender de red, de la
/// disponibilidad de Duffel ni del token.
/// </summary>
public class DuffelFlightProviderTests
{
    private const string TestToken = "duffel_test_fake-token-for-unit-tests";

    private static (DuffelFlightProvider Provider, Mock<HttpMessageHandler> Handler) Build(
        HttpStatusCode status, string body, string contentType = "application/json")
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, contentType) });

        return (BuildProvider(handler), handler);
    }

    private static DuffelFlightProvider BuildProvider(Mock<HttpMessageHandler> handler, DuffelOptions? options = null)
    {
        var duffel = options ?? new DuffelOptions { AccessToken = TestToken, TimeoutSeconds = 5 };
        return new DuffelFlightProvider(
            new HttpClient(handler.Object),
            Options.Create(new FlightsOptions { Provider = "Duffel", Duffel = duffel }),
            Mock.Of<ILogger<DuffelFlightProvider>>());
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "duffel", name));

    private static FlightSearchRequest SearchRequest() =>
        new([new FlightSliceRequest("VVI", "LPB", new DateOnly(2026, 11, 19))], Adults: 1);

    // ---------------------------------------------------------------- credenciales y cabeceras

    [Fact]
    public void ARealTokenIsRefusedUnlessSomeoneTurnsTheGuardOff()
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var live = new DuffelOptions { AccessToken = "duffel_live_something" };

        // Una reserva aérea real cuesta dinero: el adapter no arranca con un token que no sea de prueba.
        Assert.Throws<InvalidOperationException>(() => BuildProvider(handler, live));
    }

    [Fact]
    public void WithoutATokenItFailsAtStartup_NotOnTheFirstSale()
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        Assert.Throws<InvalidOperationException>(() => BuildProvider(handler, new DuffelOptions { AccessToken = "" }));
    }

    [Fact]
    public async Task EveryRequestCarriesTheBearerTokenAndTheApiVersion()
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        HttpRequestMessage? sent = null;

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Fixture("offer-request.json"), Encoding.UTF8, "application/json"),
            });

        await BuildProvider(handler).SearchAsync(SearchRequest(), CancellationToken.None);

        Assert.NotNull(sent);
        Assert.Equal("Bearer", sent!.Headers.Authorization!.Scheme);
        Assert.Equal(TestToken, sent.Headers.Authorization.Parameter);
        Assert.Equal("v2", sent.Headers.GetValues("Duffel-Version").Single());
        Assert.Contains("air/offer_requests", sent.RequestUri!.ToString());
        Assert.Contains("return_offers=true", sent.RequestUri.ToString());
    }

    [Fact]
    public async Task TheSearchBodyCarriesRouteDateCabinAndOnePassengerPerAdult()
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        string? body = null;

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => body = request.Content!.ReadAsStringAsync().Result)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Fixture("offer-request.json"), Encoding.UTF8, "application/json"),
            });

        await BuildProvider(handler).SearchAsync(
            new FlightSearchRequest(
                [new FlightSliceRequest("VVI", "LPB", new DateOnly(2026, 11, 19))],
                Adults: 2,
                FlightCabinClass.BUSINESS),
            CancellationToken.None);

        Assert.NotNull(body);
        Assert.Contains("\"origin\":\"VVI\"", body);
        Assert.Contains("\"destination\":\"LPB\"", body);
        Assert.Contains("\"departure_date\":\"2026-11-19\"", body);
        Assert.Contains("\"cabin_class\":\"business\"", body);
        // Duffel cuenta pasajeros por elemento, no con un número: dos adultos son dos entradas.
        Assert.Equal(2, body!.Split("\"type\":\"adult\"").Length - 1);
    }

    // ---------------------------------------------------------------- mapeo

    [Fact]
    public async Task AnOfferIsMappedIntoTurisClickModels()
    {
        var (provider, _) = Build(HttpStatusCode.OK, Fixture("offer-request.json"));

        var result = await provider.SearchAsync(SearchRequest(), CancellationToken.None);

        var offer = Assert.Single(result.Offers);
        Assert.Equal("orq_0000TestOfferRequest", result.SearchId);
        Assert.Equal("off_0000TestOffer", offer.Id);
        Assert.Equal(48.99m, offer.Price.Amount);
        Assert.Equal("USD", offer.Price.Currency);
        Assert.Equal("Duffel Airways", offer.OwnerName);
        Assert.Equal("ZZ", offer.OwnerIataCode);
        Assert.False(offer.LiveMode);
        Assert.False(offer.IdentityDocumentsRequired);

        var slice = Assert.Single(offer.Slices);
        Assert.Equal("VVI", slice.OriginIata);
        Assert.Equal("LPB", slice.DestinationIata);
        Assert.Equal(TimeSpan.FromMinutes(79), slice.Duration);

        var segment = Assert.Single(slice.Segments);
        Assert.Equal("ZZ", segment.MarketingCarrierIata);
        Assert.Equal("3551", segment.FlightNumber);
        Assert.Equal(1, segment.CheckedBags);
        // Hora local del aeropuerto, sin huso: 13:56 en Viru Viru es 13:56 corra donde corra el servidor.
        Assert.Equal(new DateTime(2026, 11, 19, 13, 56, 0), segment.DepartingAt);
        Assert.Equal(DateTimeKind.Unspecified, segment.DepartingAt.Kind);

        // Los ids de pasajero del proveedor son obligatorios al reservar: viajan con la oferta.
        Assert.Equal("pas_0000TestPassenger", Assert.Single(offer.Passengers).ProviderPassengerId);
    }

    [Fact]
    public async Task TheOfferExpiryTravelsAsADateAndIsComparableAgainstNow()
    {
        var (provider, _) = Build(HttpStatusCode.OK, Fixture("offer-request.json"));

        var offer = (await provider.SearchAsync(SearchRequest(), CancellationToken.None)).Offers[0];

        Assert.Equal(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero), offer.ExpiresAt);
        Assert.True(offer.IsExpired(new DateTimeOffset(2026, 11, 1, 12, 0, 1, TimeSpan.Zero)));
        Assert.False(offer.IsExpired(new DateTimeOffset(2026, 11, 1, 11, 59, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task RefreshingAnOfferReturnsThePriceTheProviderHasNow()
    {
        var (provider, _) = Build(HttpStatusCode.OK, Fixture("offer-refreshed.json"));

        var refreshed = await provider.RefreshOfferAsync("off_0000TestOffer", CancellationToken.None);

        // Es el caso que el flujo tiene que contemplar: revalidar devolvió otro precio.
        Assert.Equal(58.99m, refreshed.Price.Amount);
        Assert.True(refreshed.Price.DiffersFrom(new FlightPrice(48.99m, "USD")));
    }

    [Fact]
    public async Task CreatingAnOrderSendsTheRevalidatedPriceAndThePassengerIdsOfTheOffer()
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        string? body = null;

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => body = request.Content!.ReadAsStringAsync().Result)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Fixture("order.json"), Encoding.UTF8, "application/json"),
            });

        var order = await BuildProvider(handler).CreateOrderAsync(
            new FlightOrderRequest(
                "off_0000TestOffer",
                new FlightPrice(48.99m, "USD"),
                [new FlightPassengerDetails("pas_0000TestPassenger", "Prueba", "Demostracion", new DateOnly(1990, 1, 1), "m", "mr", "qa@example.com", "+59170000000")]),
            CancellationToken.None);

        Assert.NotNull(body);
        Assert.Contains("\"selected_offers\":[\"off_0000TestOffer\"]", body);
        Assert.Contains("\"type\":\"instant\"", body);
        // En modo de prueba el pago se declara contra el saldo de la cuenta: no hay tarjeta ni dinero real.
        Assert.Contains("\"type\":\"balance\"", body);
        Assert.Contains("\"amount\":\"48.99\"", body);
        Assert.Contains("\"born_on\":\"1990-01-01\"", body!);

        Assert.Equal("ord_0000TestOrder", order.OrderId);
        Assert.Equal("OSQLFL", order.BookingReference);
        Assert.False(order.LiveMode);
    }

    [Fact]
    public async Task ACancellationIsCreatedAndThenConfirmed()
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var paths = new List<string>();

        handler.Protected()
            .SetupSequence<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Fixture("cancellation-pending.json"), Encoding.UTF8, "application/json"),
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Fixture("cancellation-confirmed.json"), Encoding.UTF8, "application/json"),
            });

        var result = await BuildProvider(handler).CancelOrderAsync("ord_0000TestOrder", confirm: true, CancellationToken.None);

        Assert.Equal("ore_0000TestCancellation", result.CancellationId);
        Assert.Equal(48.99m, result.RefundAmount);
        Assert.Equal("balance", result.RefundTo);
        Assert.NotNull(result.ConfirmedAt);
        Assert.Empty(paths);
    }

    // ---------------------------------------------------------------- fallas del proveedor

    [Fact]
    public async Task AnExpiredOfferIsItsOwnFailure_BecauseTheAnswerIsToSearchAgain()
    {
        var (provider, _) = Build(
            HttpStatusCode.UnprocessableEntity,
            """{"errors":[{"type":"airline_error","code":"offer_no_longer_available","title":"Offer no longer available","message":"The offer is no longer available"}]}""");

        await Assert.ThrowsAsync<FlightOfferExpiredException>(
            () => provider.RefreshOfferAsync("off_0000TestOffer", CancellationToken.None));
    }

    [Fact]
    public async Task AValidationErrorKeepsTheProviderMessageAndTheStatusCode()
    {
        var (provider, _) = Build(
            HttpStatusCode.BadRequest,
            """{"errors":[{"type":"validation_error","code":"validation_required","title":"Invalid","message":"Field 'family_name' has invalid format"}]}""");

        var error = await Assert.ThrowsAsync<FlightProviderRequestException>(
            () => provider.SearchAsync(SearchRequest(), CancellationToken.None));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal("validation_required", error.ProviderCode);
        Assert.Contains("family_name", error.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task RejectedCredentialsAreAConfigurationProblem_NotATouristProblem(HttpStatusCode status)
    {
        var (provider, _) = Build(status, """{"errors":[{"type":"authentication_error","title":"Unauthorized"}]}""");

        await Assert.ThrowsAsync<FlightProviderAuthException>(
            () => provider.SearchAsync(SearchRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task AMissingOrderIsNull_NotAnException()
    {
        var (provider, _) = Build(HttpStatusCode.NotFound, """{"errors":[{"type":"not_found","title":"Not found"}]}""");

        Assert.Null(await provider.GetOrderAsync("ord_desconocida", CancellationToken.None));
    }

    [Fact]
    public async Task TooManyRequestsCarriesTheRetryAfterHint()
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("""{"errors":[{"type":"rate_limit_error","title":"Too many requests"}]}""", Encoding.UTF8, "application/json"),
        };
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        var error = await Assert.ThrowsAsync<FlightProviderRateLimitException>(
            () => BuildProvider(handler).SearchAsync(SearchRequest(), CancellationToken.None));

        Assert.Equal(TimeSpan.FromSeconds(30), error.RetryAfter);
    }

    [Fact]
    public async Task AServerErrorIsTreatedAsTheProviderBeingUnavailable()
    {
        var (provider, _) = Build(HttpStatusCode.InternalServerError, "<html>502</html>", "text/html");

        await Assert.ThrowsAsync<FlightProviderUnavailableException>(
            () => provider.SearchAsync(SearchRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task ANetworkFailureIsNotSilentlySwallowed()
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("socket cerrado"));

        await Assert.ThrowsAsync<FlightProviderUnavailableException>(
            () => BuildProvider(handler).SearchAsync(SearchRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task ATimeoutReadsAsUnavailable_NotAsACancelledOperation()
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("timeout"));

        await Assert.ThrowsAsync<FlightProviderUnavailableException>(
            () => BuildProvider(handler).SearchAsync(SearchRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task AMalformedResponseIsNeverGuessed()
    {
        var (provider, _) = Build(HttpStatusCode.OK, "{ esto no es json ");

        await Assert.ThrowsAsync<FlightProviderResponseException>(
            () => provider.SearchAsync(SearchRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task AnEmptyEnvelopeIsAResponseError_NotAnEmptyResult()
    {
        var (provider, _) = Build(HttpStatusCode.OK, """{"meta":{}}""");

        await Assert.ThrowsAsync<FlightProviderResponseException>(
            () => provider.SearchAsync(SearchRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task ASearchWithNoOffersIsAnEmptyList_NotAFailure()
    {
        var (provider, _) = Build(HttpStatusCode.OK, """{"data":{"id":"orq_vacia","offers":[]}}""");

        var result = await provider.SearchAsync(SearchRequest(), CancellationToken.None);

        Assert.Empty(result.Offers);
    }
}
