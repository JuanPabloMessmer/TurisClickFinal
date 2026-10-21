using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Admin.Dtos;
using TurisClick.Api.Modules.Companies.Dtos;
using TurisClick.Api.Modules.Flights.Dtos;
using TurisClick.Api.Modules.Flights.ProviderFlights.Dtos;
using TurisClick.Api.Modules.Reservations.Dtos;
using TurisClick.Api.Modules.Reservations.Entities;
using TurisClick.Api.Modules.Reservations.Services;
using TurisClick.Api.Shared.Responses;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// Vuelos de operador de punta a punta, contra PostgreSQL real: autorización del ADMIN, publicación, búsqueda,
/// reserva de ida o de ida y vuelta, pago simulado, emisión registrada por el operador, emisión fallida con
/// reembolso total, cancelación, expiración y la carrera por el último asiento.
///
/// La base de tests es compartida y persistente: cada escenario usa su propia empresa y una fecha lejana
/// distinta, así que la búsqueda de un test no ve los vuelos de otro.
/// </summary>
[Collection(ApiCollection.Name)]
public class ProviderFlightBookingTests(TurisClickApiFactory factory)
{
    private readonly TurisClickApiFactory _factory = factory;

    // ================================================================ escenario

    private sealed record Scenario(
        HttpClient Admin,
        HttpClient Provider,
        Guid CompanyId,
        Guid FlightId,
        Guid OutboundId,
        DateOnly Date,
        string Origin,
        string Destination);

    /// <summary>Una fecha lejana distinta por escenario: aísla las búsquedas de un test de las de los demás.</summary>
    private static DateOnly UniqueDate() =>
        DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(Random.Shared.Next(60, 320));

    private static object Flight(string origin, string destination, string number = "712", string cabin = "ECONOMY") => new
    {
        originIata = origin,
        destinationIata = destination,
        carrierName = "Aerolínea Demo",
        carrierIata = "ZZ",
        flightNumber = number,
        cabinClass = cabin,
        checkedBags = 1,
        carryOnBags = 1,
        cancellationPolicy = new[] { new { minDaysBefore = 30, refundPercentage = 100 }, new { minDaysBefore = 0, refundPercentage = 0 } },
        fareRules = "Cambios con cargo. No-show sin reembolso.",
    };

    private static object Departure(DateOnly date, int hour, int seats, decimal adult = 600m, decimal child = 450m, decimal infant = 60m)
    {
        var departure = date.ToDateTime(new TimeOnly(hour, 0));
        return new
        {
            departureAt = departure.ToString("yyyy-MM-ddTHH:mm:ss"),
            arrivalAt = departure.AddMinutes(70).ToString("yyyy-MM-ddTHH:mm:ss"),
            totalSeats = seats,
            adultPrice = adult,
            childPrice = child,
            infantPrice = infant,
            currency = "BOB",
        };
    }

    private async Task<HttpClient> AdminAsync()
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await LoginAsAdminAsync(client));
        return client;
    }

    private async Task<HttpClient> TouristAsync(string prefix)
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await RegisterAndLoginTouristAsync(client, prefix));
        return client;
    }

    /// <summary>Operador autorizado con un vuelo publicado y una salida de ida.</summary>
    private async Task<Scenario> PublishedFlightAsync(
        string prefix, int seats = 20, string origin = "CIJ", string destination = "RIB", string cabin = "ECONOMY")
    {
        var admin = await AdminAsync();
        var provider = _factory.CreateClient();
        var account = await RegisterApprovedProviderAsync(provider, _factory.CreateClient(), prefix);
        UseBearerToken(provider, account.AccessToken);

        var authorized = await admin.PutAsJsonAsync($"/api/admin/companies/{account.Company.Id}/flight-seller",
            new { authorized = true, registration = $"Registro {prefix}" });
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);

        var flight = await CreatedAsync<ProviderFlightResponse>(
            await provider.PostAsJsonAsync("/api/companies/me/flights", Flight(origin, destination, cabin: cabin)));

        var date = UniqueDate();
        var outbound = await CreatedAsync<ProviderFlightDepartureResponse>(
            await provider.PostAsJsonAsync($"/api/companies/me/flights/{flight.Id}/departures", Departure(date, 8, seats)));

        Assert.Equal(HttpStatusCode.OK, (await provider.PostAsync($"/api/companies/me/flights/{flight.Id}/publish", null)).StatusCode);

        return new Scenario(admin, provider, account.Company.Id, flight.Id, outbound.Id, date, origin, destination);
    }

    /// <summary>El vuelo de vuelta del mismo operador, publicado, dos días después.</summary>
    private async Task<Guid> PublishReturnAsync(Scenario scenario, int seats = 20)
    {
        var back = await CreatedAsync<ProviderFlightResponse>(
            await scenario.Provider.PostAsJsonAsync("/api/companies/me/flights", Flight(scenario.Destination, scenario.Origin, "713")));
        var departure = await CreatedAsync<ProviderFlightDepartureResponse>(
            await scenario.Provider.PostAsJsonAsync($"/api/companies/me/flights/{back.Id}/departures", Departure(scenario.Date.AddDays(2), 18, seats)));
        Assert.Equal(HttpStatusCode.OK, (await scenario.Provider.PostAsync($"/api/companies/me/flights/{back.Id}/publish", null)).StatusCode);
        return departure.Id;
    }

    private static object Passenger(string type, string bornOn, string name = "Prueba") =>
        new { type, givenName = name, familyName = "Demostración", bornOn };

    private static readonly object[] OneAdult = [Passenger("ADULT", "1990-01-01")];

    /// <summary>
    /// Dos adultos, un niño de 7 y un infante: tres asientos. El infante nació hace dos meses (una fecha de
    /// nacimiento tiene que ser pasada) y sigue siendo menor de 2 el día del vuelo, que es a lo sumo en 320 días.
    /// </summary>
    private static object[] Family(DateOnly flightDate) =>
    [
        Passenger("ADULT", "1985-03-10", "Ana"),
        Passenger("ADULT", "1987-07-21", "Luis"),
        Passenger("CHILD", flightDate.AddYears(-7).ToString("yyyy-MM-dd"), "Sofía"),
        Passenger("INFANT", DateOnly.FromDateTime(DateTime.UtcNow.Date).AddMonths(-2).ToString("yyyy-MM-dd"), "Mateo"),
    ];

    private static Task<HttpResponseMessage> ReserveAsync(HttpClient tourist, Guid[] departures, object[] passengers, int seats) =>
        tourist.PostAsJsonAsync("/api/reservations", new { flightDepartureIds = departures, travelers = seats, passengers });

    private static async Task<T> CreatedAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
            $"esperaba 201/200 y vino {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private static async Task<ReservationResponse> PayAsync(HttpClient tourist, Guid reservationId)
    {
        var paid = await tourist.PostAsJsonAsync($"/api/reservations/{reservationId}/pay", new { success = true });
        return await CreatedAsync<ReservationResponse>(paid);
    }

    private async Task<T> DbAsync<T>(Func<TurisClickDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<TurisClickDbContext>());
    }

    private Task<int> ReservedSeatsAsync(Guid departureId) =>
        DbAsync(db => db.ProviderFlightDepartures.AsNoTracking().Where(d => d.Id == departureId).Select(d => d.ReservedSeats).FirstAsync());

    private Task<SearchFlightsResponse> SearchAsync(HttpClient client, Scenario s, object? extra = null, DateOnly? back = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["originIata"] = s.Origin,
            ["destinationIata"] = s.Destination,
            ["departureDate"] = s.Date.ToString("yyyy-MM-dd"),
            ["returnDate"] = back?.ToString("yyyy-MM-dd"),
            ["adults"] = 1,
        };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties()) body[property.Name] = property.GetValue(extra);

        return client.PostAsJsonAsync("/api/flights/search", body)
            .ContinueWith(t => t.Result.Content.ReadFromJsonAsync<SearchFlightsResponse>(JsonOptions)).Unwrap()!;
    }

    // ================================================================ autorización y catálogo

    [Fact]
    public async Task SinAutorizacionNoSeVende_YRevocarSacaLosVuelosDeLaBusqueda()
    {
        var s = await PublishedFlightAsync("pfb-auth");

        var company = await (await s.Provider.GetAsync("/api/companies/me")).Content.ReadFromJsonAsync<CompanyResponse>(JsonOptions);
        Assert.True(company!.CanSellFlights);
        Assert.Equal("Registro pfb-auth", company.FlightSellerRegistration);
        Assert.NotNull(company.FlightSellerAuthorizedAt);

        Assert.Contains((await SearchAsync(_factory.CreateClient(), s)).Options, o => o.ProviderFlight?.DepartureIds.Contains(s.OutboundId) == true);

        await s.Admin.PutAsJsonAsync($"/api/admin/companies/{s.CompanyId}/flight-seller", new { authorized = false });

        var list = await s.Provider.GetAsync("/api/companies/me/flights");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Contains("FLIGHT_SELLER_NOT_AUTHORIZED", await list.Content.ReadAsStringAsync());
        Assert.DoesNotContain((await SearchAsync(_factory.CreateClient(), s)).Options, o => o.ProviderFlight?.DepartureIds.Contains(s.OutboundId) == true);
    }

    [Fact]
    public async Task PublicarExigeUnaSalidaFuturaConAsientos()
    {
        var admin = await AdminAsync();
        var provider = _factory.CreateClient();
        var account = await RegisterApprovedProviderAsync(provider, _factory.CreateClient(), "pfb-publish");
        UseBearerToken(provider, account.AccessToken);
        await admin.PutAsJsonAsync($"/api/admin/companies/{account.Company.Id}/flight-seller", new { authorized = true, registration = "R-1" });

        var flight = await CreatedAsync<ProviderFlightResponse>(await provider.PostAsJsonAsync("/api/companies/me/flights", Flight("CIJ", "RIB")));
        Assert.Equal("DRAFT", flight.Status);
        Assert.True(flight.Demo);

        Assert.Equal(HttpStatusCode.Conflict, (await provider.PostAsync($"/api/companies/me/flights/{flight.Id}/publish", null)).StatusCode);
    }

    [Fact]
    public async Task UnOperadorNoVeNiTocaLosVuelosDeOtro()
    {
        var s = await PublishedFlightAsync("pfb-owner-a");
        var other = await PublishedFlightAsync("pfb-owner-b");

        Assert.Equal(HttpStatusCode.Forbidden, (await other.Provider.GetAsync($"/api/companies/me/flights/{s.FlightId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.Provider.PostAsync($"/api/companies/me/flight-departures/{s.OutboundId}/close", null)).StatusCode);

        var mine = await (await s.Provider.GetAsync("/api/companies/me/flights")).Content.ReadFromJsonAsync<List<ProviderFlightResponse>>(JsonOptions);
        Assert.Equal([s.FlightId], mine!.Select(f => f.Id));
    }

    // ================================================================ búsqueda

    [Fact]
    public async Task LaBusquedaMuestraElVueloDeOperadorReservable_JuntoALasOfertasDeAerolinea()
    {
        var s = await PublishedFlightAsync("pfb-search");

        var result = await SearchAsync(_factory.CreateClient(), s, new { adults = 2, childrenAges = new[] { 7 }, infants = 1 });

        // La base es compartida: puede haber vuelos de otras corridas en la misma ruta. Se mira el de este escenario.
        var option = Assert.Single(result.Options, o => o.ProviderFlight?.DepartureIds.Contains(s.OutboundId) == true);
        Assert.Equal(FlightOfferSources.Provider, option.Source);
        Assert.True(option.Bookable);
        Assert.True(result.Bookable);
        Assert.True(option.ProviderFlight!.Demo);
        Assert.Equal([s.OutboundId], option.ProviderFlight!.DepartureIds);
        Assert.Equal(20, option.ProviderFlight.SeatsLeft);
        Assert.Equal(s.CompanyId, option.ProviderFlight.OperatorCompanyId);
        // 2 adultos × 600 + 1 niño × 450 + 1 infante × 60.
        Assert.Equal(1710m, option.Price.Amount);
        Assert.Equal("BOB", option.Price.Currency);
        Assert.Equal(s.Origin, option.Slices.Single().OriginIata);

        Assert.Contains(result.Options, o => o.Source == FlightOfferSources.Airline && !o.Bookable);
    }

    [Fact]
    public async Task OtraCabinaOUnVueloSuspendidoNoAparecen()
    {
        var s = await PublishedFlightAsync("pfb-search-filter");

        bool Mine(FlightOptionResponse o) => o.ProviderFlight?.DepartureIds.Contains(s.OutboundId) == true;

        Assert.DoesNotContain((await SearchAsync(_factory.CreateClient(), s, new { cabinClass = "BUSINESS" })).Options, Mine);

        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostAsync($"/api/admin/flights/{s.FlightId}/suspend", null)).StatusCode);
        Assert.DoesNotContain((await SearchAsync(_factory.CreateClient(), s)).Options, Mine);

        var page = await (await s.Admin.GetAsync($"/api/admin/flights?companyId={s.CompanyId}&suspended=true"))
            .Content.ReadFromJsonAsync<PagedResult<ProviderFlightResponse>>(JsonOptions);
        Assert.Equal(s.FlightId, Assert.Single(page!.Items).Id);

        await s.Admin.PostAsync($"/api/admin/flights/{s.FlightId}/unsuspend", null);
        Assert.Contains((await SearchAsync(_factory.CreateClient(), s)).Options, Mine);
    }

    [Fact]
    public async Task IdaYVueltaSeOfreceComoParDelMismoOperador()
    {
        var s = await PublishedFlightAsync("pfb-search-rt");
        var back = await PublishReturnAsync(s);

        var result = await SearchAsync(_factory.CreateClient(), s, back: s.Date.AddDays(2));

        var option = Assert.Single(result.Options, o => o.ProviderFlight?.OperatorCompanyId == s.CompanyId);
        Assert.Equal([s.OutboundId, back], option.ProviderFlight!.DepartureIds);
        Assert.Equal(2, option.Slices.Count);
        Assert.Equal(1200m, option.Price.Amount);
    }

    // ================================================================ reserva y pago

    [Fact]
    public async Task ReservarTomaAsientosPorPasajeroConAsiento_YCongelaPrecioYPasajeros()
    {
        var s = await PublishedFlightAsync("pfb-reserve");
        var tourist = await TouristAsync("pfb-reserve-t");

        var reservation = await CreatedAsync<ReservationResponse>(await ReserveAsync(tourist, [s.OutboundId], Family(s.Date), seats: 3));

        Assert.Equal("PENDING_PAYMENT", reservation.Status);
        var item = Assert.Single(reservation.Items);
        Assert.Equal("FLIGHT", item.ProductType);
        Assert.Equal(1710m, item.Subtotal);
        Assert.Equal(3, item.Travelers);
        Assert.Equal("NOT_REQUIRED", item.Flight!.IssuanceStatus);
        Assert.Equal(["Ana", "Luis", "Sofía", "Mateo"], item.Flight.Passengers.Select(p => p.GivenName));
        Assert.Equal("INFANT", item.Flight.Passengers[3].Type);
        Assert.True(item.Flight.Demo);
        Assert.Equal(s.Date, item.Date);

        // Los infantes viajan en brazos: tres asientos, no cuatro.
        Assert.Equal(3, await ReservedSeatsAsync(s.OutboundId));
    }

    [Fact]
    public async Task UnPasajeroCuyaEdadNoCorrespondeASuTipoSeRechaza()
    {
        var s = await PublishedFlightAsync("pfb-ages");
        var tourist = await TouristAsync("pfb-ages-t");

        var response = await ReserveAsync(tourist, [s.OutboundId],
            [Passenger("ADULT", "1990-01-01"), Passenger("CHILD", s.Date.AddYears(-13).ToString("yyyy-MM-dd"))], seats: 2);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await ReservedSeatsAsync(s.OutboundId));
    }

    [Fact]
    public async Task PagarConfirmaYDejaLaEmisionPendienteDelOperador()
    {
        var s = await PublishedFlightAsync("pfb-pay");
        var tourist = await TouristAsync("pfb-pay-t");
        var reservation = await CreatedAsync<ReservationResponse>(await ReserveAsync(tourist, [s.OutboundId], OneAdult, 1));

        var paid = await PayAsync(tourist, reservation.Id);

        Assert.Equal("CONFIRMED", paid.Status);
        Assert.Equal("CONFIRMED", paid.Items[0].Status);
        Assert.Equal("PENDING", paid.Items[0].Flight!.IssuanceStatus);
        Assert.Empty(paid.Items[0].Flight!.TicketNumbers);

        // El operador lo ve en sus reservas recibidas, con los pasajeros.
        var received = await (await s.Provider.GetAsync($"/api/companies/me/reservations/{paid.Items[0].Id}"))
            .Content.ReadFromJsonAsync<ReservationItemResponse>(JsonOptions);
        Assert.Equal("PENDING", received!.Flight!.IssuanceStatus);
        Assert.Single(received.Flight.Passengers);
    }

    [Fact]
    public async Task IdaYVuelta_UnTramoCanceladoPorElOperadorNoSeCobraNiRevive()
    {
        var s = await PublishedFlightAsync("pfb-rt-pay");
        var back = await PublishReturnAsync(s);
        var tourist = await TouristAsync("pfb-rt-pay-t");

        var reservation = await CreatedAsync<ReservationResponse>(await ReserveAsync(tourist, [s.OutboundId, back], OneAdult, 1));
        Assert.Equal(2, reservation.Items.Count);
        var outboundItem = reservation.Items.Single(i => i.Flight!.DepartureId == s.OutboundId);
        var returnItem = reservation.Items.Single(i => i.Flight!.DepartureId == back);

        var cancelled = await s.Provider.PostAsJsonAsync(
            $"/api/companies/me/reservations/{outboundItem.Id}/cancel", new { reason = "El avión quedó fuera de servicio." });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(0, await ReservedSeatsAsync(s.OutboundId));

        var paid = await PayAsync(tourist, reservation.Id);

        Assert.Equal("CANCELLED", paid.Items.Single(i => i.Id == outboundItem.Id).Status);
        Assert.Equal("NOT_REQUIRED", paid.Items.Single(i => i.Id == outboundItem.Id).Flight!.IssuanceStatus);
        Assert.Equal("PENDING", paid.Items.Single(i => i.Id == returnItem.Id).Flight!.IssuanceStatus);

        var charged = await DbAsync(db => db.PaymentTransactions.AsNoTracking()
            .Where(p => p.ReservationId == reservation.Id && p.Type == PaymentTransactionType.CHARGE && p.Status == PaymentTransactionStatus.SUCCEEDED)
            .SumAsync(p => p.Amount));
        Assert.Equal(returnItem.Subtotal, charged);
    }

    [Fact]
    public async Task LaEdadSeValidaEnCadaTramo_UnInfanteQueCumple2AntesDeLaVueltaNecesitaAsiento()
    {
        var s = await PublishedFlightAsync("pfb-rt-ages");
        var back = await PublishReturnAsync(s);
        var tourist = await TouristAsync("pfb-rt-ages-t");

        // Cumple 2 años el día después de la ida: es infante a la ida y ya no a la vuelta (dos días después).
        var infant = Passenger("INFANT", s.Date.AddDays(1).AddYears(-2).ToString("yyyy-MM-dd"), "Mateo");
        var response = await ReserveAsync(tourist, [s.OutboundId, back], [Passenger("ADULT", "1990-01-01"), infant], seats: 1);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("antes de la vuelta", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await ReservedSeatsAsync(s.OutboundId));

        // Sólo ida, el mismo pasajero es un infante válido.
        Assert.Equal(HttpStatusCode.Created,
            (await ReserveAsync(tourist, [s.OutboundId], [Passenger("ADULT", "1990-01-01"), infant], seats: 1)).StatusCode);
    }

    [Fact]
    public async Task UnaVueltaQueSaleAntesDeLlegarLaIdaSeRechaza()
    {
        var s = await PublishedFlightAsync("pfb-rt-order");
        var back = await PublishReturnAsync(s);
        var tourist = await TouristAsync("pfb-rt-order-t");

        // Al revés: la "ida" sería la vuelta, que sale dos días después.
        var response = await ReserveAsync(tourist, [back, s.OutboundId], OneAdult, 1);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ================================================================ cupo

    [Fact]
    public async Task SinAsientosSuficientesNoSeReservaNada()
    {
        var s = await PublishedFlightAsync("pfb-capacity", seats: 2);
        var tourist = await TouristAsync("pfb-capacity-t");

        var response = await ReserveAsync(tourist, [s.OutboundId], Family(s.Date), seats: 3);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await ReservedSeatsAsync(s.OutboundId));
    }

    [Fact]
    public async Task IdaYVueltaSinLugarEnLaVueltaTampocoTomaLaIda()
    {
        var s = await PublishedFlightAsync("pfb-capacity-rt", seats: 5);
        var back = await PublishReturnAsync(s, seats: 1);
        var tourist = await TouristAsync("pfb-capacity-rt-t");

        var response = await ReserveAsync(tourist, [s.OutboundId, back],
            [Passenger("ADULT", "1990-01-01"), Passenger("ADULT", "1991-01-01")], seats: 2);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await ReservedSeatsAsync(s.OutboundId));
        Assert.Equal(0, await ReservedSeatsAsync(back));
    }

    [Fact]
    public async Task DosTuristasNoPuedenQuedarseConElMismoUltimoAsiento()
    {
        var s = await PublishedFlightAsync("pfb-race", seats: 2);
        var tourists = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => TouristAsync($"pfb-race-{i}")));

        var responses = await Task.WhenAll(tourists.Select(t => ReserveAsync(t, [s.OutboundId], OneAdult, 1)));

        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(2, await ReservedSeatsAsync(s.OutboundId));
    }

    [Fact]
    public async Task LaExpiracionDevuelveLosAsientos()
    {
        var s = await PublishedFlightAsync("pfb-expire");
        var tourist = await TouristAsync("pfb-expire-t");
        var reservation = await CreatedAsync<ReservationResponse>(await ReserveAsync(tourist, [s.OutboundId], Family(s.Date), 3));
        Assert.Equal(3, await ReservedSeatsAsync(s.OutboundId));

        await DbAsync(db => db.Reservations.Where(r => r.Id == reservation.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));

        using (var scope = _factory.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<IReservationExpirationService>().ExpireAsync(reservation.Id, CancellationToken.None));

        Assert.Equal(0, await ReservedSeatsAsync(s.OutboundId));
    }

    [Fact]
    public async Task CancelarSinPagarDevuelveLosAsientos()
    {
        var s = await PublishedFlightAsync("pfb-cancel-pending");
        var tourist = await TouristAsync("pfb-cancel-pending-t");
        var reservation = await CreatedAsync<ReservationResponse>(await ReserveAsync(tourist, [s.OutboundId], OneAdult, 1));

        Assert.Equal(HttpStatusCode.OK, (await tourist.PostAsync($"/api/reservations/{reservation.Id}/cancel", null)).StatusCode);

        Assert.Equal(0, await ReservedSeatsAsync(s.OutboundId));
    }

    // ================================================================ edición con reservas

    [Fact]
    public async Task ConReservasNoSeCambiaLoVendido_NiSeBajanAsientosDebajoDeLoVendido()
    {
        var s = await PublishedFlightAsync("pfb-edit", seats: 10);
        var tourist = await TouristAsync("pfb-edit-t");
        await CreatedAsync<ReservationResponse>(await ReserveAsync(tourist, [s.OutboundId], Family(s.Date), 3));

        Assert.Equal(HttpStatusCode.Conflict,
            (await s.Provider.PutAsJsonAsync($"/api/companies/me/flights/{s.FlightId}", Flight(s.Origin, s.Destination, number: "999"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await s.Provider.PutAsJsonAsync($"/api/companies/me/flight-departures/{s.OutboundId}", Departure(s.Date, 8, 10, adult: 900m))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await s.Provider.PutAsJsonAsync($"/api/companies/me/flight-departures/{s.OutboundId}", Departure(s.Date, 8, 2))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await s.Provider.DeleteAsync($"/api/companies/me/flight-departures/{s.OutboundId}")).StatusCode);

        // Sumar asientos sí se puede, y la política o las condiciones también.
        var more = await CreatedAsync<ProviderFlightDepartureResponse>(
            await s.Provider.PutAsJsonAsync($"/api/companies/me/flight-departures/{s.OutboundId}", Departure(s.Date, 8, 15)));
        Assert.Equal(12, more.AvailableSeats);
        Assert.True(more.HasReservations);

        var closed = await CreatedAsync<ProviderFlightDepartureResponse>(
            await s.Provider.PostAsync($"/api/companies/me/flight-departures/{s.OutboundId}/close", null));
        Assert.Equal("CLOSED", closed.Status);
    }

    // ================================================================ emisión

    private async Task<(HttpClient Tourist, ReservationResponse Paid, Scenario S)> PaidFamilyAsync(string prefix)
    {
        var s = await PublishedFlightAsync(prefix);
        var tourist = await TouristAsync($"{prefix}-t");
        var reservation = await CreatedAsync<ReservationResponse>(await ReserveAsync(tourist, [s.OutboundId], Family(s.Date), 3));
        return (tourist, await PayAsync(tourist, reservation.Id), s);
    }

    [Fact]
    public async Task LaEmisionLaRegistraElOperador_UnPasajePorPasajero_YRecienAhiQuedaEmitido()
    {
        var (tourist, paid, s) = await PaidFamilyAsync("pfb-issue");
        var itemId = paid.Items[0].Id;

        // Cuatro pasajeros (infante incluido): con tres números no alcanza.
        var short3 = await s.Provider.PostAsJsonAsync($"/api/companies/me/reservation-items/{itemId}/issuance",
            new { ticketNumbers = new[] { "9302400000001", "9302400000002", "9302400000003" } });
        Assert.Equal(HttpStatusCode.BadRequest, short3.StatusCode);

        // Otra empresa no emite lo que no vendió.
        var other = await PublishedFlightAsync("pfb-issue-other");
        Assert.Equal(HttpStatusCode.Forbidden, (await other.Provider.PostAsJsonAsync(
            $"/api/companies/me/reservation-items/{itemId}/issuance",
            new { ticketNumbers = new[] { "9302400000001", "9302400000002", "9302400000003", "9302400000004" } })).StatusCode);

        var issued = await CreatedAsync<FlightItemIssuanceResponse>(await s.Provider.PostAsJsonAsync(
            $"/api/companies/me/reservation-items/{itemId}/issuance",
            new { ticketNumbers = new[] { "9302400000001", "9302400000002", "9302400000003", "9302400000004" }, bookingReference = "abc123" }));
        Assert.Equal("ISSUED", issued.IssuanceStatus);
        Assert.Equal("ABC123", issued.BookingReference);

        var seen = await (await tourist.GetAsync($"/api/reservations/{paid.Id}")).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        Assert.Equal("ISSUED", seen!.Items[0].Flight!.IssuanceStatus);
        Assert.Equal(4, seen.Items[0].Flight!.TicketNumbers.Count);
        Assert.NotNull(seen.Items[0].Flight!.IssuedAt);

        // Registrar dos veces no pisa la primera.
        Assert.Equal(HttpStatusCode.Conflict, (await s.Provider.PostAsJsonAsync(
            $"/api/companies/me/reservation-items/{itemId}/issuance",
            new { ticketNumbers = new[] { "1111111", "2222222", "3333333", "4444444" } })).StatusCode);
    }

    [Fact]
    public async Task NoSeEmiteUnVueloSinPagar()
    {
        var s = await PublishedFlightAsync("pfb-issue-unpaid");
        var tourist = await TouristAsync("pfb-issue-unpaid-t");
        var reservation = await CreatedAsync<ReservationResponse>(await ReserveAsync(tourist, [s.OutboundId], OneAdult, 1));

        var response = await s.Provider.PostAsJsonAsync(
            $"/api/companies/me/reservation-items/{reservation.Items[0].Id}/issuance", new { ticketNumbers = new[] { "9302400000001" } });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task EmisionFallida_CancelaConReembolsoTotal_YDevuelveLosAsientos()
    {
        var (tourist, paid, s) = await PaidFamilyAsync("pfb-issue-fail");
        var item = paid.Items[0];
        Assert.Equal(3, await ReservedSeatsAsync(s.OutboundId));

        var failed = await CreatedAsync<FlightItemIssuanceResponse>(await s.Provider.PostAsJsonAsync(
            $"/api/companies/me/reservation-items/{item.Id}/issuance-failure", new { reason = "La aerolínea canceló la ruta." }));
        Assert.Equal("FAILED", failed.IssuanceStatus);

        var seen = await (await tourist.GetAsync($"/api/reservations/{paid.Id}")).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        Assert.Equal("CANCELLED", seen!.Status);
        Assert.Equal("CANCELLED", seen.Items[0].Status);
        Assert.Equal("FAILED", seen.Items[0].Flight!.IssuanceStatus);
        Assert.Equal(item.Subtotal, seen.Items[0].RefundedAmount);
        Assert.Equal(0, await ReservedSeatsAsync(s.OutboundId));

        var refund = await DbAsync(db => db.PaymentTransactions.AsNoTracking().SingleAsync(p =>
            p.ReservationId == paid.Id && p.Type == PaymentTransactionType.REFUND));
        Assert.Equal(PaymentTransactionStatus.SUCCEEDED, refund.Status);
        Assert.Equal(PaymentComponent.PROVIDER_FLIGHT, refund.Component);
        Assert.Equal(item.Subtotal, refund.Amount);

        // Repetirlo no devuelve dos veces.
        Assert.Equal(HttpStatusCode.Conflict, (await s.Provider.PostAsJsonAsync(
            $"/api/companies/me/reservation-items/{item.Id}/issuance-failure", new { reason = "Otra vez." })).StatusCode);
    }

    [Fact]
    public async Task CancelarAntesDeLaEmisionDevuelveTodo_AunqueLaPoliticaNoLoHaria()
    {
        var (tourist, paid, s) = await PaidFamilyAsync("pfb-cancel-unissued");

        var quote = await CreatedAsync<CancellationQuoteResponse>(await tourist.PostAsync($"/api/reservations/{paid.Id}/cancellation-quote", null));
        var line = Assert.Single(quote.Lines);
        Assert.Equal("PROVIDER_FLIGHT", line.Component);
        Assert.Equal(100, line.RefundPercentage);
        Assert.Equal(paid.Items[0].Subtotal, line.RefundAmount);

        // /cancel devuelve la reserva ya cancelada, con lo que se devolvió por línea.
        var cancelled = await CreatedAsync<ReservationResponse>(
            await tourist.PostAsJsonAsync($"/api/reservations/{paid.Id}/cancel", new { cancellationQuoteId = quote.QuoteId }));
        Assert.Equal("CANCELLED", cancelled.Status);
        Assert.Equal(paid.Items[0].Subtotal, cancelled.Items[0].RefundedAmount);
        Assert.Equal("COMPLETED", cancelled.Cancellation!.Status);
        Assert.Equal(0, await ReservedSeatsAsync(s.OutboundId));
    }

    [Fact]
    public async Task ConElPasajeEmitidoAplicaLaPoliticaDelOperador()
    {
        var (tourist, paid, s) = await PaidFamilyAsync("pfb-cancel-issued");
        await CreatedAsync<FlightItemIssuanceResponse>(await s.Provider.PostAsJsonAsync(
            $"/api/companies/me/reservation-items/{paid.Items[0].Id}/issuance",
            new { ticketNumbers = new[] { "9302400000001", "9302400000002", "9302400000003", "9302400000004" } }));

        var quote = await CreatedAsync<CancellationQuoteResponse>(await tourist.PostAsync($"/api/reservations/{paid.Id}/cancellation-quote", null));

        // La salida está a más de 30 días: la política del vuelo devuelve el 100%; lo que importa es que se
        // aplicó la política y no la regla de "sin emitir".
        var line = Assert.Single(quote.Lines);
        Assert.Contains("días de anticipación", line.Explanation);
        Assert.DoesNotContain("todavía no emitió", line.Explanation);
    }

    // ================================================================ admin

    [Fact]
    public async Task ElAdminVeLosVuelosPagadosSinEmitir_YLosAtrasadosPidenAtencion()
    {
        var (_, paid, s) = await PaidFamilyAsync("pfb-admin");

        var flights = await (await s.Admin.GetAsync($"/api/admin/reservations?productType=FLIGHT&pageSize=100"))
            .Content.ReadFromJsonAsync<PagedResult<AdminReservationRowResponse>>(JsonOptions);
        var row = Assert.Single(flights!.Items, r => r.Id == paid.Id);
        Assert.True(row.FlightIssuancePending);
        Assert.False(row.FlightIssuanceOverdue);
        Assert.StartsWith("Vuelo 712", row.Summary);

        await DbAsync(db => db.Reservations.Where(r => r.Id == paid.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.ConfirmedAt, DateTimeOffset.UtcNow.AddHours(-25))));

        var attention = await (await s.Admin.GetAsync("/api/admin/reservations?needsAttention=true&pageSize=100"))
            .Content.ReadFromJsonAsync<PagedResult<AdminReservationRowResponse>>(JsonOptions);
        Assert.True(Assert.Single(attention!.Items, r => r.Id == paid.Id).FlightIssuanceOverdue);
    }
}
