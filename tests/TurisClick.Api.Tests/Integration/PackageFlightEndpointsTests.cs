using System.Net;
using System.Net.Http.Json;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Flights.Dtos;
using TurisClick.Api.Modules.Packages.Dtos;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// Vuelos en paquetes de proveedor: configurar la regla, cotizar contra el proveedor y revalidar.
///
/// Corren con el proveedor FALSO (`Flights:Provider` queda en su default en el entorno de tests), así
/// que **ningún test llama a Duffel**. Lo que se protege es lo que haría perder plata o confianza: que
/// el precio lo decida el servidor, que no se sumen monedas distintas, que un operador no toque
/// paquetes ajenos, y que una cotización vencida se diga vencida en lugar de venderse.
/// </summary>
[Collection(ApiCollection.Name)]
public class PackageFlightEndpointsTests(TurisClickApiFactory factory)
{
    private readonly TurisClickApiFactory _factory = factory;

    private async Task<Guid> CreateCityDestinationAsync(HttpClient adminClient)
    {
        UseBearerToken(adminClient, await LoginAsAdminAsync(adminClient));
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var country = await (await adminClient.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"País-{suffix}", type = "COUNTRY" })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var region = await (await adminClient.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"Región-{suffix}", type = "REGION", parentId = country!.Id })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var city = await (await adminClient.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"Ciudad-{suffix}", type = "CITY", parentId = region!.Id })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);

        return city!.Id;
    }

    /// <summary>Un paquete publicado con una salida futura y cupo: el escenario mínimo para cotizar un vuelo.</summary>
    private async Task<(HttpClient Provider, PackageResponse Package, Guid AvailabilityId)> CreatePublishedPackageAsync(
        string emailPrefix, string currency = "USD")
    {
        var adminClient = _factory.CreateClient();
        var destinationId = await CreateCityDestinationAsync(adminClient);

        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), emailPrefix);
        UseBearerToken(providerClient, provider.AccessToken);

        var package = await (await providerClient.PostAsJsonAsync("/api/packages", new
        {
            title = $"Jordania Mágica-{Guid.NewGuid():N}",
            description = "Siete días por Jordania con guía local y traslados incluidos.",
            destinationId,
            categoryIds = Array.Empty<Guid>(),
            durationDays = 7,
            price = 1090m,
            currency,
            items = new[] { new { dayNumber = 1, sortOrder = 1, kind = "DESCRIPTIVE", title = "Llegada y traslado" } },
            images = Array.Empty<object>(),
        })).Content.ReadFromJsonAsync<PackageResponse>(JsonOptions);

        var departure = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(60);
        var availability = await (await providerClient.PostAsJsonAsync($"/api/packages/{package!.Id}/availability", new
        {
            departureDate = departure.ToString("yyyy-MM-dd"),
            totalSlots = 10,
        })).Content.ReadFromJsonAsync<PackageAvailabilityResponse>(JsonOptions);

        var publish = await providerClient.PostAsync($"/api/packages/{package.Id}/publish", null);
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);

        return (providerClient, package, availability!.Id);
    }

    private static object RuleBody(string destination = "AMM", object? origins = null) => new
    {
        destinationIata = destination,
        allowedOriginIatas = origins ?? new[] { "VVI", "LPB" },
        cabinClass = "ECONOMY",
        outboundOffsetDays = -1,
        inboundOffsetDays = 0,
        roundTrip = true,
    };

    // ---------------------------------------------------------------- regla del operador

    [Fact]
    public async Task ElOperadorConfiguraLaReglaYElPaqueteQuedaConVuelo()
    {
        var (providerClient, package, _) = await CreatePublishedPackageAsync("flight-rule-ok");

        var response = await providerClient.PutAsJsonAsync($"/api/packages/{package.Id}/flight-rule", RuleBody());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rule = await response.Content.ReadFromJsonAsync<PackageFlightRuleResponse>(JsonOptions);
        Assert.Equal("AMM", rule!.DestinationIata);
        Assert.Equal("Amán (AMM)", rule.DestinationLabel);
        Assert.Equal(["VVI", "LPB"], rule.AllowedOrigins.Select(o => o.Iata));

        // El catálogo público refleja que incluye vuelo.
        var publicClient = _factory.CreateClient();
        var published = await (await publicClient.GetAsync($"/api/packages/{package.Id}")).Content
            .ReadFromJsonAsync<PackageResponse>(JsonOptions);
        Assert.True(published!.IncludesFlight);
    }

    [Fact]
    public async Task LosCodigosIataSeNormalizanAMayusculas()
    {
        var (providerClient, package, _) = await CreatePublishedPackageAsync("flight-rule-normalize");

        var response = await providerClient.PutAsJsonAsync($"/api/packages/{package.Id}/flight-rule",
            RuleBody("amm", new[] { "vvi", "lpb" }));

        var rule = await response.Content.ReadFromJsonAsync<PackageFlightRuleResponse>(JsonOptions);
        Assert.Equal("AMM", rule!.DestinationIata);
        Assert.Equal(["VVI", "LPB"], rule.AllowedOrigins.Select(o => o.Iata));
    }

    [Fact]
    public async Task UnCodigoInvalidoSeRechazaConUn400()
    {
        var (providerClient, package, _) = await CreatePublishedPackageAsync("flight-rule-invalid");

        var response = await providerClient.PutAsJsonAsync($"/api/packages/{package.Id}/flight-rule",
            RuleBody("AMAN"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ElDestinoNoPuedeEstarTambienEntreLosOrigenes()
    {
        var (providerClient, package, _) = await CreatePublishedPackageAsync("flight-rule-same");

        var response = await providerClient.PutAsJsonAsync($"/api/packages/{package.Id}/flight-rule",
            RuleBody("VVI", new[] { "VVI", "LPB" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnOperadorNoPuedeConfigurarElPaqueteDeOtraEmpresa()
    {
        var (_, package, _) = await CreatePublishedPackageAsync("flight-rule-owner");

        var intruderClient = _factory.CreateClient();
        var intruder = await RegisterApprovedProviderAsync(intruderClient, _factory.CreateClient(), "flight-rule-intruder");
        UseBearerToken(intruderClient, intruder.AccessToken);

        var response = await intruderClient.PutAsJsonAsync($"/api/packages/{package.Id}/flight-rule", RuleBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnTuristaNoPuedeConfigurarReglasDeVuelo()
    {
        var (_, package, _) = await CreatePublishedPackageAsync("flight-rule-tourist");

        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "flight-rule-t"));

        var response = await touristClient.PutAsJsonAsync($"/api/packages/{package.Id}/flight-rule", RuleBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ElAdminVeLaReglaDeCualquierPaquete()
    {
        var (providerClient, package, _) = await CreatePublishedPackageAsync("flight-rule-admin");
        await providerClient.PutAsJsonAsync($"/api/packages/{package.Id}/flight-rule", RuleBody());

        // Visibilidad global: el ADMIN modera la plataforma y no está limitado por la empresa dueña.
        var adminClient = _factory.CreateClient();
        UseBearerToken(adminClient, await LoginAsAdminAsync(adminClient));

        var response = await adminClient.GetAsync($"/api/packages/{package.Id}/flight-rule");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rule = await response.Content.ReadFromJsonAsync<PackageFlightRuleResponse>(JsonOptions);
        Assert.Equal("AMM", rule!.DestinationIata);
    }

    [Fact]
    public async Task QuitarLaReglaDejaElPaqueteSinVuelo()
    {
        var (providerClient, package, _) = await CreatePublishedPackageAsync("flight-rule-remove");
        await providerClient.PutAsJsonAsync($"/api/packages/{package.Id}/flight-rule", RuleBody());

        var deleted = await providerClient.DeleteAsync($"/api/packages/{package.Id}/flight-rule");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var published = await (await _factory.CreateClient().GetAsync($"/api/packages/{package.Id}")).Content
            .ReadFromJsonAsync<PackageResponse>(JsonOptions);
        Assert.False(published!.IncludesFlight);
    }

    // ---------------------------------------------------------------- cotización

    [Fact]
    public async Task CotizarDevuelvePrecioDelPaqueteDelVueloYElTotal()
    {
        var (providerClient, package, availabilityId) = await CreatePublishedPackageAsync("flight-quote-ok");
        await providerClient.PutAsJsonAsync($"/api/packages/{package.Id}/flight-rule", RuleBody());

        // El catálogo es público: se puede cotizar sin sesión, igual que se puede ver un precio.
        var publicClient = _factory.CreateClient();
        var response = await publicClient.PostAsJsonAsync($"/api/packages/{package.Id}/flight-quotes",
            new { originIata = "VVI", packageAvailabilityId = availabilityId, travelers = 2 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quote = await response.Content.ReadFromJsonAsync<PackageFlightQuoteResponse>(JsonOptions);

        Assert.Equal(1090m, quote!.PackagePrice.Amount);
        Assert.Equal("USD", quote.PackagePrice.Currency);
        Assert.NotEmpty(quote.Options);
        Assert.Equal("Santa Cruz de la Sierra (VVI)", quote.OriginLabel);

        var option = quote.Options[0];
        Assert.True(option.FlightPrice.Amount > 0);
        Assert.NotNull(option.CombinedTotal);
        Assert.Equal(quote.PackagePrice.Amount + option.FlightPrice.Amount, option.CombinedTotal!.Amount);
        Assert.NotEmpty(option.Slices);
        Assert.NotEqual(Guid.Empty, option.QuoteId);
    }

    [Fact]
    public async Task LasFechasDelVueloSalenDeLaSalidaDelPaquete()
    {
        var (providerClient, package, availabilityId) = await CreatePublishedPackageAsync("flight-quote-dates");
        await providerClient.PutAsJsonAsync($"/api/packages/{package.Id}/flight-rule", RuleBody());

        var quote = await (await _factory.CreateClient().PostAsJsonAsync($"/api/packages/{package.Id}/flight-quotes",
            new { originIata = "VVI", packageAvailabilityId = availabilityId, travelers = 1 }))
            .Content.ReadFromJsonAsync<PackageFlightQuoteResponse>(JsonOptions);

        var departure = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(60);
        // Offset de ida -1 => el día anterior; 7 días de viaje con offset de vuelta 0 => el último día.
        Assert.Equal(departure.AddDays(-1), quote!.OutboundDate);
        Assert.Equal(departure.AddDays(6), quote.InboundDate);
    }

    [Fact]
    public async Task UnPaqueteSinVueloNoSePuedeCotizar()
    {
        var (_, package, availabilityId) = await CreatePublishedPackageAsync("flight-quote-noflight");

        var response = await _factory.CreateClient().PostAsJsonAsync($"/api/packages/{package.Id}/flight-quotes",
            new { originIata = "VVI", packageAvailabilityId = availabilityId, travelers = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnOrigenQueElOperadorNoOfreceSeRechaza()
    {
        var (providerClient, package, availabilityId) = await CreatePublishedPackageAsync("flight-quote-origin");
        await providerClient.PutAsJsonAsync($"/api/packages/{package.Id}/flight-rule", RuleBody("AMM", new[] { "VVI" }));

        var response = await _factory.CreateClient().PostAsJsonAsync($"/api/packages/{package.Id}/flight-quotes",
            new { originIata = "CBB", packageAvailabilityId = availabilityId, travelers = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ConMonedasDistintasNoSeInventaUnTotalCombinado()
    {
        // El paquete cotiza en bolivianos y el proveedor de vuelos en dólares: no hay tipo de cambio en
        // este sistema, así que no puede haber un total sumado.
        var (providerClient, package, availabilityId) = await CreatePublishedPackageAsync("flight-quote-fx", currency: "BOB");
        await providerClient.PutAsJsonAsync($"/api/packages/{package.Id}/flight-rule", RuleBody());

        var quote = await (await _factory.CreateClient().PostAsJsonAsync($"/api/packages/{package.Id}/flight-quotes",
            new { originIata = "VVI", packageAvailabilityId = availabilityId, travelers = 1 }))
            .Content.ReadFromJsonAsync<PackageFlightQuoteResponse>(JsonOptions);

        var option = quote!.Options[0];
        Assert.Equal("BOB", quote.PackagePrice.Currency);
        Assert.Equal("USD", option.FlightPrice.Currency);
        Assert.Null(option.CombinedTotal);
    }

    [Fact]
    public async Task LaRespuestaNoFiltraElIdentificadorDeOfertaDelProveedor()
    {
        var (providerClient, package, availabilityId) = await CreatePublishedPackageAsync("flight-quote-leak");
        await providerClient.PutAsJsonAsync($"/api/packages/{package.Id}/flight-rule", RuleBody());

        var body = await (await _factory.CreateClient().PostAsJsonAsync($"/api/packages/{package.Id}/flight-quotes",
            new { originIata = "VVI", packageAvailabilityId = availabilityId, travelers = 1 })).Content.ReadAsStringAsync();

        // El cliente recibe NUESTRO id de cotización; el del proveedor se queda en el backend, que es lo
        // que impide que alguien reserve una oferta arbitraria.
        Assert.DoesNotContain("off_fake", body);
        Assert.Contains("quoteId", body);
    }

    [Fact]
    public async Task UnaSalidaDeOtroPaqueteNoSirveParaCotizar()
    {
        var (providerClient, package, _) = await CreatePublishedPackageAsync("flight-quote-wrong-dep");
        await providerClient.PutAsJsonAsync($"/api/packages/{package.Id}/flight-rule", RuleBody());

        var (_, _, otherAvailabilityId) = await CreatePublishedPackageAsync("flight-quote-other-dep");

        var response = await _factory.CreateClient().PostAsJsonAsync($"/api/packages/{package.Id}/flight-quotes",
            new { originIata = "VVI", packageAvailabilityId = otherAvailabilityId, travelers = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------- revalidación

    [Fact]
    public async Task RevalidarUnaCotizacionVigenteLaConfirma()
    {
        var (providerClient, package, availabilityId) = await CreatePublishedPackageAsync("flight-reval-ok");
        await providerClient.PutAsJsonAsync($"/api/packages/{package.Id}/flight-rule", RuleBody());

        var client = _factory.CreateClient();
        var quote = await (await client.PostAsJsonAsync($"/api/packages/{package.Id}/flight-quotes",
            new { originIata = "VVI", packageAvailabilityId = availabilityId, travelers = 1 }))
            .Content.ReadFromJsonAsync<PackageFlightQuoteResponse>(JsonOptions);

        var response = await client.PostAsync($"/api/flight-quotes/{quote!.Options[0].QuoteId}/revalidate", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<FlightQuoteRevalidationResponse>(JsonOptions);
        Assert.Equal("UNCHANGED", result!.Outcome);
        Assert.False(result.RequiresAcceptance);
        Assert.Equal(result.PreviousPrice.Amount, result.CurrentPrice!.Amount);
    }

    [Fact]
    public async Task UnCambioDePrecioDevuelveLosDosImportesYExigeAceptacion()
    {
        var (providerClient, package, availabilityId) = await CreatePublishedPackageAsync("flight-reval-change");
        // CHG es la ruta con la que el proveedor falso simula un cambio de precio al revalidar.
        await providerClient.PutAsJsonAsync($"/api/packages/{package.Id}/flight-rule", RuleBody("AMM", new[] { "CHG" }));

        var client = _factory.CreateClient();
        var quote = await (await client.PostAsJsonAsync($"/api/packages/{package.Id}/flight-quotes",
            new { originIata = "CHG", packageAvailabilityId = availabilityId, travelers = 1 }))
            .Content.ReadFromJsonAsync<PackageFlightQuoteResponse>(JsonOptions);

        var result = await (await client.PostAsync($"/api/flight-quotes/{quote!.Options[0].QuoteId}/revalidate", null))
            .Content.ReadFromJsonAsync<FlightQuoteRevalidationResponse>(JsonOptions);

        Assert.Equal("PRICE_CHANGED", result!.Outcome);
        Assert.True(result.RequiresAcceptance);
        Assert.True(result.CurrentPrice!.Amount > result.PreviousPrice.Amount);
        Assert.Contains("subió", result.Message);
    }

    [Fact]
    public async Task UnaOfertaQueElProveedorYaNoTieneSeInformaComoNoDisponible()
    {
        var (providerClient, package, availabilityId) = await CreatePublishedPackageAsync("flight-reval-gone");
        await providerClient.PutAsJsonAsync($"/api/packages/{package.Id}/flight-rule", RuleBody("AMM", new[] { "GON" }));

        var client = _factory.CreateClient();
        var quote = await (await client.PostAsJsonAsync($"/api/packages/{package.Id}/flight-quotes",
            new { originIata = "GON", packageAvailabilityId = availabilityId, travelers = 1 }))
            .Content.ReadFromJsonAsync<PackageFlightQuoteResponse>(JsonOptions);

        var result = await (await client.PostAsync($"/api/flight-quotes/{quote!.Options[0].QuoteId}/revalidate", null))
            .Content.ReadFromJsonAsync<FlightQuoteRevalidationResponse>(JsonOptions);

        Assert.Equal("UNAVAILABLE", result!.Outcome);
        Assert.Null(result.CurrentPrice);
    }

    [Fact]
    public async Task UnaCotizacionInexistenteDevuelve404()
    {
        var response = await _factory.CreateClient().PostAsync($"/api/flight-quotes/{Guid.NewGuid()}/revalidate", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------- aeropuertos

    [Fact]
    public async Task ElCatalogoDeAeropuertosOfreceLosBolivianosPrimero()
    {
        var response = await _factory.CreateClient().GetAsync("/api/airports?country=BO");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var airports = await response.Content.ReadFromJsonAsync<List<AirportResponse>>(JsonOptions);
        Assert.Contains(airports!, a => a.Iata == "VVI");
        Assert.Contains(airports!, a => a.Iata == "LPB");
        Assert.Contains(airports!, a => a.Iata == "CBB");
        Assert.DoesNotContain(airports!, a => a.Iata == "JFK");
    }
}
