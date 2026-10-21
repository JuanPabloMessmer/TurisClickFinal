using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TurisClick.Api.Modules.Flights.Dtos;
using TurisClick.Api.Modules.Flights.ProviderFlights.Dtos;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// Contratos de los vuelos de operador: quién puede llamar a cada endpoint y qué pedidos se rechazan antes de
/// tocar nada. Los flujos completos (reservar, pagar, emitir, cancelar) están en ProviderFlightBookingTests.
/// </summary>
[Collection(ApiCollection.Name)]
public class ProviderFlightContractTests(TurisClickApiFactory factory)
{
    private readonly TurisClickApiFactory _factory = factory;

    private static readonly Guid AnyId = Guid.NewGuid();

    private static object ValidFlight => new
    {
        originIata = "LPB",
        destinationIata = "UYU",
        carrierName = "Aerolínea de Prueba",
        carrierIata = "ZZ",
        flightNumber = "712",
        cabinClass = "ECONOMY",
        checkedBags = 1,
        carryOnBags = 1,
        cancellationPolicy = new[] { new { minDaysBefore = 7, refundPercentage = 100 }, new { minDaysBefore = 0, refundPercentage = 0 } },
    };

    private static object ValidDeparture
    {
        get
        {
            var departure = DateTime.UtcNow.Date.AddDays(20).AddHours(8);
            return new
            {
                departureAt = departure.ToString("yyyy-MM-ddTHH:mm:ss"),
                arrivalAt = departure.AddMinutes(70).ToString("yyyy-MM-ddTHH:mm:ss"),
                totalSeats = 40,
                adultPrice = 650m,
                childPrice = 500m,
                infantPrice = 65m,
                currency = "BOB",
            };
        }
    }

    /// <summary>Los endpoints del catálogo del operador: exigen empresa autorizada a vender vuelos.</summary>
    public static TheoryData<string, string, string?> CatalogEndpoints => new()
    {
        { "GET", "/api/companies/me/flights", null },
        { "POST", "/api/companies/me/flights", "flight" },
        { "GET", $"/api/companies/me/flights/{AnyId}", null },
        { "PUT", $"/api/companies/me/flights/{AnyId}", "flight" },
        { "POST", $"/api/companies/me/flights/{AnyId}/publish", null },
        { "POST", $"/api/companies/me/flights/{AnyId}/archive", null },
        { "GET", $"/api/companies/me/flights/{AnyId}/departures", null },
        { "POST", $"/api/companies/me/flights/{AnyId}/departures", "departure" },
        { "PUT", $"/api/companies/me/flight-departures/{AnyId}", "departure" },
        { "DELETE", $"/api/companies/me/flight-departures/{AnyId}", null },
        { "POST", $"/api/companies/me/flight-departures/{AnyId}/close", null },
    };

    private static object? BodyFor(string? kind) => kind switch
    {
        "flight" => ValidFlight,
        "departure" => ValidDeparture,
        "issuance" => new { ticketNumbers = new[] { "9302412345678" }, bookingReference = "ABC123" },
        "failure" => new { reason = "La aerolínea cerró el vuelo." },
        "seller" => new { authorized = true, registration = "Registro de agencia 1234" },
        _ => null,
    };

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string url, object? body)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (body is not null) request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    private async Task<HttpClient> ProviderAsync(string prefix)
    {
        var client = _factory.CreateClient();
        var registration = await RegisterApprovedProviderAsync(client, _factory.CreateClient(), prefix);
        UseBearerToken(client, registration.AccessToken);
        return client;
    }

    private async Task<HttpClient> TouristAsync(string prefix)
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await RegisterAndLoginTouristAsync(client, prefix));
        return client;
    }

    private async Task<HttpClient> AdminAsync()
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await LoginAsAdminAsync(client));
        return client;
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }

    // ---------------------------------------------------------------- permisos

    [Theory]
    [MemberData(nameof(CatalogEndpoints))]
    public async Task ElCatalogoDeVuelosExigeOperadorAutorizadoAVenderVuelos(string method, string url, string? body)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(_factory.CreateClient(), method, url, BodyFor(body))).StatusCode);

        var tourist = await TouristAsync("pf-contract-tourist");
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(tourist, method, url, BodyFor(body))).StatusCode);

        // Una empresa aprobada pero no autorizada a vender vuelos recibe un 403 con su código, que el
        // Backoffice usa para explicar que la autoriza un ADMIN.
        var provider = await ProviderAsync("pf-contract-provider");
        var response = await SendAsync(provider, method, url, BodyFor(body));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("FLIGHT_SELLER_NOT_AUTHORIZED", await ErrorCodeAsync(response));
    }

    [Theory]
    [InlineData("issuance")]
    [InlineData("issuance-failure")]
    public async Task LaEmisionDeUnItemQueNoExisteEs404(string action)
    {
        var provider = await ProviderAsync("pf-contract-issue");

        var response = await provider.PostAsJsonAsync(
            $"/api/companies/me/reservation-items/{AnyId}/{action}", BodyFor(action == "issuance" ? "issuance" : "failure"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LosEndpointsDeAdminExigenAdmin()
    {
        var provider = await ProviderAsync("pf-contract-admin-p");
        Assert.Equal(HttpStatusCode.Forbidden, (await provider.GetAsync("/api/admin/flights")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await provider.PutAsJsonAsync($"/api/admin/companies/{AnyId}/flight-seller", BodyFor("seller"))).StatusCode);

        var admin = await AdminAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/flights")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PutAsJsonAsync($"/api/admin/companies/{AnyId}/flight-seller", BodyFor("seller"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/admin/flights/{AnyId}/suspend", null)).StatusCode);
    }

    // ---------------------------------------------------------------- validación de entrada

    [Theory]
    [InlineData("LPB", "LIM")] // el destino tiene que estar en Bolivia
    [InlineData("VVI", "SRZ")] // misma ciudad
    [InlineData("ZZZ", "UYU")] // aeropuerto desconocido
    public async Task UnVueloConRutaInvalidaSeRechaza(string origin, string destination)
    {
        var provider = await ProviderAsync("pf-contract-route");

        var response = await provider.PostAsJsonAsync("/api/companies/me/flights", new
        {
            originIata = origin,
            destinationIata = destination,
            carrierName = "Aerolínea de Prueba",
            flightNumber = "712",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnaSalidaQueLlegaAntesDeSalirSeRechaza()
    {
        var provider = await ProviderAsync("pf-contract-dep");
        var departure = DateTime.UtcNow.Date.AddDays(20).AddHours(8);

        var response = await provider.PostAsJsonAsync($"/api/companies/me/flights/{AnyId}/departures", new
        {
            departureAt = departure.ToString("yyyy-MM-ddTHH:mm:ss"),
            arrivalAt = departure.AddMinutes(-10).ToString("yyyy-MM-ddTHH:mm:ss"),
            totalSeats = 40,
            adultPrice = 650m,
            childPrice = 500m,
            infantPrice = 65m,
            currency = "BOB",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AutorizarAUnaEmpresaSinSuRegistroSeRechaza()
    {
        var admin = await AdminAsync();

        var response = await admin.PutAsJsonAsync($"/api/admin/companies/{AnyId}/flight-seller", new { authorized = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------------------------------------------------------- la reserva del turista

    private static object FlightReservation(object[] passengers, int travelers, Guid[]? departures = null) => new
    {
        flightDepartureIds = departures ?? [Guid.NewGuid(), Guid.NewGuid()],
        travelers,
        passengers,
    };

    private static object Passenger(string type, string bornOn) =>
        new { type, givenName = "Prueba", familyName = "Demostración", bornOn };

    [Fact]
    public async Task ReservarUnVueloExigeSesion_YUnaSalidaInexistenteEs404()
    {
        var body = FlightReservation([Passenger("ADULT", "1990-01-01")], travelers: 1, [Guid.NewGuid()]);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsJsonAsync("/api/reservations", body)).StatusCode);

        var tourist = await TouristAsync("pf-contract-book");
        Assert.Equal(HttpStatusCode.NotFound, (await tourist.PostAsJsonAsync("/api/reservations", body)).StatusCode);
    }

    [Fact]
    public async Task UnaReservaDeVueloConOtroProductoSeRechaza()
    {
        var tourist = await TouristAsync("pf-contract-mixed");

        var response = await tourist.PostAsJsonAsync("/api/reservations", new
        {
            flightDepartureIds = new[] { Guid.NewGuid() },
            experienceAvailabilityId = Guid.NewGuid(),
            travelers = 1,
            passengers = new[] { Passenger("ADULT", "1990-01-01") },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public static TheoryData<string, object[], int> InvalidPassengerSets => new()
    {
        { "sin pasajeros", [], 1 },
        { "sin adultos", [Passenger("CHILD", "2018-05-05")], 1 },
        { "más infantes que adultos", [Passenger("ADULT", "1990-01-01"), Passenger("INFANT", "2026-01-01"), Passenger("INFANT", "2026-02-01")], 1 },
        { "travelers no cuenta los asientos", [Passenger("ADULT", "1990-01-01"), Passenger("INFANT", "2026-01-01")], 2 },
        { "nacido en el futuro", [Passenger("ADULT", "2999-01-01")], 1 },
    };

    [Theory]
    [MemberData(nameof(InvalidPassengerSets))]
    public async Task LasReglasDePasajerosSeValidanAlReservar(string caso, object[] passengers, int travelers)
    {
        var tourist = await TouristAsync("pf-contract-pax");

        var response = await tourist.PostAsJsonAsync("/api/reservations", FlightReservation(passengers, travelers));

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{caso}: esperaba 400 y vino {(int)response.StatusCode}");
    }

    [Fact]
    public async Task MasDeDosSalidasORepetidasSeRechazan()
    {
        var tourist = await TouristAsync("pf-contract-legs");
        var same = Guid.NewGuid();
        var adult = new[] { Passenger("ADULT", "1990-01-01") };

        Assert.Equal(HttpStatusCode.BadRequest,
            (await tourist.PostAsJsonAsync("/api/reservations", FlightReservation(adult, 1, [same, same]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await tourist.PostAsJsonAsync("/api/reservations", FlightReservation(adult, 1, [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()]))).StatusCode);
    }

    // ---------------------------------------------------------------- la búsqueda

    [Fact]
    public async Task LasOfertasDeAerolineaNoSonReservables()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/flights/search", new
        {
            originIata = "MAD",
            destinationIata = "VVI",
            departureDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(30)).ToString("yyyy-MM-dd"),
            adults = 1,
        });
        var result = await response.Content.ReadFromJsonAsync<SearchFlightsResponse>(JsonOptions);

        var airline = result!.Options.Where(o => o.Source == FlightOfferSources.Airline).ToList();
        Assert.NotEmpty(airline);
        Assert.All(airline, option =>
        {
            Assert.False(option.Bookable);
            Assert.Null(option.ProviderFlight);
        });
    }
}
