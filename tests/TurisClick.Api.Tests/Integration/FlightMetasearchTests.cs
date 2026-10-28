using System.Net;
using System.Net.Http.Json;
using TurisClick.Api.Modules.Flights.Dtos;
using TurisClick.Api.Modules.Flights.ProviderFlights.Dtos;
using TurisClick.Api.Modules.Flights.Services;
using TurisClick.Api.Shared.Exceptions;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// Oleada 19: los vuelos son un metabuscador. Se busca en TurisClick y se compra en el sitio del vendedor.
/// Se protege que (1) ninguna oferta se reserve, pague ni emita dentro de TurisClick, (2) las ofertas DEMO
/// nunca lleven un enlace de compra y (3) un enlace sólo se abra si es https y de un dominio autorizado.
/// Corre con la configuración de producción: vuelos de operador apagados.
/// </summary>
[Collection(ApiCollection.Name)]
public class FlightMetasearchTests(TurisClickApiFactory factory)
{
    private readonly TurisClickApiFactory _factory = factory;

    private static object Search(string? returnDate = null) => new
    {
        originIata = "MAD",
        destinationIata = "VVI",
        departureDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(30)).ToString("yyyy-MM-dd"),
        returnDate,
        adults = 1,
        cabinClass = "ECONOMY",
    };

    private async Task<SearchFlightsResponse> SearchAsync(object body)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/flights/search", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SearchFlightsResponse>(JsonOptions))!;
    }

    [Fact]
    public async Task LasOfertasDemoNoSeReservanNiRedirigen()
    {
        var result = await SearchAsync(Search());

        Assert.NotEmpty(result.Options);
        Assert.False(result.Bookable);
        Assert.Equal(FlightInventoryModes.Simulated, result.InventoryMode);
        Assert.All(result.Options, o =>
        {
            Assert.Equal(FlightOfferSources.Airline, o.Source);
            Assert.False(o.Bookable);
            Assert.Null(o.ProviderFlight);
            Assert.NotNull(o.Redirect);
            Assert.False(o.Redirect!.Available);
            Assert.Null(o.Redirect.Url);
            Assert.Equal(FlightRedirectReasons.Demo, o.Redirect.UnavailableReason);
        });
    }

    [Fact]
    public async Task IdaYVueltaTambienEsSoloBusqueda()
    {
        var back = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(37)).ToString("yyyy-MM-dd");
        var result = await SearchAsync(Search(back));

        Assert.NotEmpty(result.Options);
        Assert.All(result.Options, o =>
        {
            Assert.Equal(2, o.Slices.Count);
            Assert.False(o.Bookable);
            Assert.False(o.Redirect!.Available);
        });
    }

    [Fact]
    public async Task NoHayCheckoutInternoDeVuelos()
    {
        var tourist = _factory.CreateClient();
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, "metasearch-nobook"));

        var response = await tourist.PostAsJsonAsync("/api/reservations", new
        {
            flightDepartureIds = new[] { Guid.NewGuid() },
            travelers = 1,
            passengers = new[] { new { type = "ADULT", givenName = "Ana", familyName = "Demo", bornOn = "1990-01-01" } },
        });

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Contains(ErrorCodes.FeatureNotAvailable, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LosEndpointsDeVuelosDeOperadorEstanApagados()
    {
        var provider = _factory.CreateClient();
        var account = await CreateProviderAccountAsync(provider, approve: true, "metasearch-provider");
        UseBearerToken(provider, account.AccessToken);
        Assert.Equal(HttpStatusCode.NotImplemented, (await provider.GetAsync("/api/companies/me/flights")).StatusCode);
        Assert.Equal(HttpStatusCode.NotImplemented,
            (await provider.PostAsJsonAsync($"/api/companies/me/reservation-items/{Guid.NewGuid()}/issuance", new { })).StatusCode);

        var admin = _factory.CreateClient();
        UseBearerToken(admin, await LoginAsAdminAsync(admin));
        Assert.Equal(HttpStatusCode.NotImplemented, (await admin.GetAsync("/api/admin/flights")).StatusCode);
        Assert.Equal(HttpStatusCode.NotImplemented,
            (await admin.PutAsJsonAsync($"/api/admin/companies/{Guid.NewGuid()}/flight-seller", new { authorized = true })).StatusCode);

        // Sin sesión sigue siendo 401: apagar la funcionalidad no abre ni filtra nada.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/admin/flights")).StatusCode);
    }

    // ------------------------------------------------------------------ política de redirección

    private static readonly string[] Allowed = ["www.aerolinea-demo.com", "reservas.agencia-demo.bo"];

    private static FlightOffer Offer(string? url, bool live = true, string? seller = null) =>
        new("off_1", new FlightPrice(100, "USD"), [], null, false, true, live, "Aerolínea Demo", "ZZ", [])
        {
            BookingUrl = url,
            SellerName = seller,
        };

    [Theory]
    [InlineData("https://www.aerolinea-demo.com/compra?o=1")]
    [InlineData("https://WWW.AEROLINEA-DEMO.COM/compra")]
    [InlineData("https://reservas.agencia-demo.bo:443/x#y")]
    public void UnEnlaceHttpsDeUnDominioAutorizadoSeAbre(string url)
    {
        var redirect = FlightRedirectPolicy.Evaluate(Offer(url, seller: "Agencia Demo"), simulated: false, Allowed);

        Assert.True(redirect.Available);
        Assert.StartsWith("https://", redirect.Url);
        Assert.Equal("Agencia Demo", redirect.SellerName);
        Assert.Null(redirect.UnavailableReason);
    }

    [Theory]
    [InlineData("http://www.aerolinea-demo.com/compra")]                     // sin TLS
    [InlineData("javascript:alert(1)")]                                       // esquema
    [InlineData("data:text/html,hola")]
    [InlineData("ftp://www.aerolinea-demo.com/")]
    [InlineData("https://www.aerolinea-demo.com.atacante.net/compra")]        // subdominio engañoso
    [InlineData("https://evil.www.aerolinea-demo.com/compra")]                // subdominio no autorizado
    [InlineData("https://aerolinea-demo.com/compra")]                         // padre no autorizado
    [InlineData("https://www.aerolinea-demo.com@atacante.net/compra")]        // usuario en la URL
    [InlineData("https://atacante.net#@www.aerolinea-demo.com")]
    [InlineData("https://www.aerolinea-demo.com:8443/compra")]                // otro puerto
    [InlineData("https://203.0.113.10/compra")]                                // IP
    [InlineData("https://www.aerolinea-demo.com\\@atacante.net/")]            // barra invertida
    [InlineData("https://www.aerolinea-demo.com/ compra")]                    // espacio
    [InlineData("https://www.aerolinea-demo.com./compra")]                    // punto final
    [InlineData("//www.aerolinea-demo.com/compra")]                           // relativa al esquema
    [InlineData("/compra")]
    public void UnEnlaceInseguroOManipuladoNoSeAbre(string url)
    {
        var redirect = FlightRedirectPolicy.Evaluate(Offer(url), simulated: false, Allowed);

        Assert.False(redirect.Available);
        Assert.Null(redirect.Url);
        Assert.Equal(FlightRedirectReasons.LinkNotAllowed, redirect.UnavailableReason);
    }

    [Fact]
    public void UnaOfertaDemoNuncaRedirigeAunqueTraigaEnlace()
    {
        var url = "https://www.aerolinea-demo.com/compra";

        Assert.Equal(FlightRedirectReasons.Demo, FlightRedirectPolicy.Evaluate(Offer(url), simulated: true, Allowed).UnavailableReason);
        Assert.Equal(FlightRedirectReasons.Demo, FlightRedirectPolicy.Evaluate(Offer(url, live: false), simulated: false, Allowed).UnavailableReason);
        Assert.Null(FlightRedirectPolicy.Evaluate(Offer(url), simulated: true, Allowed).Url);
    }

    [Fact]
    public void SinEnlaceOSinDominiosAutorizadosNoHayRedireccion()
    {
        Assert.Equal(FlightRedirectReasons.NoLink, FlightRedirectPolicy.Evaluate(Offer(null), false, Allowed).UnavailableReason);
        Assert.Equal(FlightRedirectReasons.NoLink, FlightRedirectPolicy.Evaluate(Offer("  "), false, Allowed).UnavailableReason);
        Assert.Equal(FlightRedirectReasons.LinkNotAllowed,
            FlightRedirectPolicy.Evaluate(Offer("https://www.aerolinea-demo.com/x"), false, []).UnavailableReason);
        Assert.Equal("Aerolínea Demo", FlightRedirectPolicy.Evaluate(Offer(null), false, Allowed).SellerName);
    }

    [Fact]
    public void LosDominiosAutorizadosSeLeenPorProveedor()
    {
        var options = new FlightsOptions();
        options.RedirectHosts["Duffel"] = ["www.aerolinea-demo.com"];

        Assert.Single(options.RedirectHostsFor("duffel"));
        Assert.Empty(options.RedirectHostsFor("Fake"));
        Assert.False(options.ProviderFlights.Enabled);
    }
}
