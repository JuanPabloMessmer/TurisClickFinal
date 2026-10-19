using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Flights.Dtos;
using TurisClick.Api.Modules.Flights.Services;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// Búsqueda de vuelos sin paquete — el explorador de la app.
///
/// Corre con el proveedor FALSO, igual que el resto de los tests de vuelos: ninguno llama a Duffel.
///
/// Lo que se protege acá es la frontera del dominio: esta búsqueda **no vende pasajes**. No persiste
/// cotizaciones, no devuelve ningún identificador reservable, y lo único comprable que ofrece son los
/// paquetes que vuelan la ruta. Si alguna vez aparece un id de cotización en esta respuesta, alguien
/// convirtió un explorador en una tienda sin darse cuenta.
/// </summary>
[Collection(ApiCollection.Name)]
public class FlightSearchEndpointTests(TurisClickApiFactory factory)
{
    private readonly TurisClickApiFactory _factory = factory;

    private async Task<HttpClient> CreateTouristClientAsync(string emailPrefix)
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await RegisterAndLoginTouristAsync(client, emailPrefix));
        return client;
    }

    private async Task<int> FlightQuoteCountAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TurisClickDbContext>();
        return await db.FlightQuotes.CountAsync();
    }

    private static object Body(
        string origin = "VVI",
        string destination = "LPB",
        int inDays = 20,
        int passengers = 1,
        string? returnDate = null) => new
        {
            originIata = origin,
            destinationIata = destination,
            departureDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(inDays)).ToString("yyyy-MM-dd"),
            returnDate,
            passengers,
            cabinClass = "ECONOMY",
        };

    [Fact]
    public async Task BuscarVuelosDevuelveOfertasConAerolineaPrecioYEscalas()
    {
        var tourist = await CreateTouristClientAsync("flight-search-ok");

        var response = await tourist.PostAsJsonAsync("/api/flights/search", Body());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SearchFlightsResponse>(JsonOptions);

        Assert.Equal("Santa Cruz de la Sierra (VVI)", result!.OriginLabel);
        Assert.Equal("La Paz (LPB)", result.DestinationLabel);
        Assert.NotEmpty(result.Options);

        var option = result.Options[0];
        Assert.True(option.Price.Amount > 0);
        Assert.False(string.IsNullOrWhiteSpace(option.Price.Currency));
        var slice = Assert.Single(option.Slices);
        Assert.Equal("VVI", slice.OriginIata);
        Assert.Equal("LPB", slice.DestinationIata);
        Assert.NotEmpty(slice.Segments);
        Assert.True(slice.Stops >= 0);
    }

    /// <summary>
    /// La garantía central: esta respuesta no puede llevar nada reservable. El tipo no tiene siquiera un
    /// campo donde meterlo, y este test existe para que agregarlo duela.
    /// </summary>
    [Fact]
    public async Task LaBusquedaNoDevuelveNadaReservableNiPersisteCotizaciones()
    {
        var tourist = await CreateTouristClientAsync("flight-search-noquote");
        var antes = await FlightQuoteCountAsync();

        var response = await tourist.PostAsJsonAsync("/api/flights/search", Body());
        var result = await response.Content.ReadFromJsonAsync<SearchFlightsResponse>(JsonOptions);

        Assert.NotEmpty(result!.Options);
        Assert.DoesNotContain("quoteId", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        // Y nada quedó escrito. Se mide como diferencia porque la base de tests es compartida: otros casos
        // cotizan esta misma ruta, y un conteo absoluto estaría midiéndolos a ellos.
        Assert.Equal(antes, await FlightQuoteCountAsync());
    }

    [Fact]
    public async Task LasOfertasDePruebaVienenRotuladasComoTales()
    {
        var tourist = await CreateTouristClientAsync("flight-search-testmode");

        var response = await tourist.PostAsJsonAsync("/api/flights/search", Body());
        var result = await response.Content.ReadFromJsonAsync<SearchFlightsResponse>(JsonOptions);

        // El proveedor falso nunca es modo real; Duffel Sandbox tampoco. La app lo rotula a partir de esto.
        Assert.True(result!.TestMode);
    }

    [Fact]
    public async Task UnaVueltaAgregaElTramoDeRegreso()
    {
        var tourist = await CreateTouristClientAsync("flight-search-roundtrip");
        var back = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(27)).ToString("yyyy-MM-dd");

        var response = await tourist.PostAsJsonAsync("/api/flights/search", Body(returnDate: back));
        var result = await response.Content.ReadFromJsonAsync<SearchFlightsResponse>(JsonOptions);

        var option = Assert.IsType<FlightOptionResponse>(result!.Options[0]);
        Assert.Equal(2, option.Slices.Count);
        Assert.Equal("LPB", option.Slices[1].OriginIata);
        Assert.Equal("VVI", option.Slices[1].DestinationIata);
    }

    /// <summary>
    /// El origen "sin ofertas" del proveedor falso es XXX, que no es un aeropuerto real y la validación lo
    /// rechaza antes de llamar a nadie — eso ya lo cubre el test del aeropuerto desconocido. Para llegar al
    /// camino de "el proveedor contestó y no había nada" hay que sustituir el proveedor.
    /// </summary>
    [Fact]
    public async Task SinOfertasSeExplicaEnVezDeDevolverUnaListaVacia()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddScoped<IFlightProvider, SinOfertasProvider>()));

        var tourist = factory.CreateClient();
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, "flight-search-empty"));

        var response = await tourist.PostAsJsonAsync("/api/flights/search", Body());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SearchFlightsResponse>(JsonOptions);

        Assert.Empty(result!.Options);
        Assert.False(string.IsNullOrWhiteSpace(result.Notice));
    }

    /// <summary>
    /// Y si el proveedor falla, tampoco puede parecerse a "no hay vuelos": son dos cosas distintas y el
    /// viajero tiene que poder distinguirlas.
    /// </summary>
    [Fact]
    public async Task SiElProveedorFallaSeDiceEnVezDeParecerQueNoHayVuelos()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddScoped<IFlightProvider, ProveedorCaidoProvider>()));

        var tourist = factory.CreateClient();
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, "flight-search-down"));

        var response = await tourist.PostAsJsonAsync("/api/flights/search", Body());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SearchFlightsResponse>(JsonOptions);

        Assert.Empty(result!.Options);
        Assert.Contains("no pudimos", result.Notice!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnaFechaPasadaSeRechaza()
    {
        var tourist = await CreateTouristClientAsync("flight-search-past");

        var response = await tourist.PostAsJsonAsync("/api/flights/search", Body(inDays: -1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ElMismoAeropuertoDeOrigenYDestinoSeRechaza()
    {
        var tourist = await CreateTouristClientAsync("flight-search-same");

        var response = await tourist.PostAsJsonAsync("/api/flights/search", Body(destination: "VVI"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnAeropuertoDesconocidoSeRechazaAntesDeLlamarAlProveedor()
    {
        var tourist = await CreateTouristClientAsync("flight-search-unknown");

        var response = await tourist.PostAsJsonAsync("/api/flights/search", Body(destination: "ZZZ"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BuscarVuelosExigeSesion()
    {
        var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/flights/search", Body());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

/// <summary>Proveedor que contesta bien pero sin inventario.</summary>
file class SinOfertasProvider : IFlightProvider
{
    public string Name => "SinOfertas";

    public Task<FlightSearchResult> SearchAsync(FlightSearchRequest request, CancellationToken ct) =>
        Task.FromResult(new FlightSearchResult("srch_vacio", [], TimeSpan.Zero));

    public Task<FlightOffer> RefreshOfferAsync(string offerId, CancellationToken ct) => throw new NotSupportedException();
    public Task<FlightOrderResult> CreateOrderAsync(FlightOrderRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task<FlightOrderResult?> GetOrderAsync(string orderId, CancellationToken ct) => throw new NotSupportedException();
    public Task<FlightOrderResult?> FindOrderByOfferAsync(string offerId, string? correlationKey, CancellationToken ct) => throw new NotSupportedException();
    public Task<FlightCancellationResult> QuoteCancellationAsync(string orderId, CancellationToken ct) => throw new NotSupportedException();
    public Task<FlightCancellationResult> ConfirmCancellationAsync(string cancellationId, CancellationToken ct) => throw new NotSupportedException();
}

/// <summary>Proveedor que no responde.</summary>
file class ProveedorCaidoProvider : IFlightProvider
{
    public string Name => "Caido";

    public Task<FlightSearchResult> SearchAsync(FlightSearchRequest request, CancellationToken ct) =>
        throw new FlightProviderUnavailableException("El proveedor no respondio.");

    public Task<FlightOffer> RefreshOfferAsync(string offerId, CancellationToken ct) => throw new NotSupportedException();
    public Task<FlightOrderResult> CreateOrderAsync(FlightOrderRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task<FlightOrderResult?> GetOrderAsync(string orderId, CancellationToken ct) => throw new NotSupportedException();
    public Task<FlightOrderResult?> FindOrderByOfferAsync(string offerId, string? correlationKey, CancellationToken ct) => throw new NotSupportedException();
    public Task<FlightCancellationResult> QuoteCancellationAsync(string orderId, CancellationToken ct) => throw new NotSupportedException();
    public Task<FlightCancellationResult> ConfirmCancellationAsync(string cancellationId, CancellationToken ct) => throw new NotSupportedException();
}
