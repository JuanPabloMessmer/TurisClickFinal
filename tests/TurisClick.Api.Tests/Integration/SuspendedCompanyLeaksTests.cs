using System.Net;
using System.Net.Http.Json;
using TurisClick.Api.Modules.Ai.Dtos;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Modules.Flights.Dtos;
using TurisClick.Api.Modules.Packages.Dtos;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// UC-A-08 en los dos caminos que llegaron después de la suspensión de empresas: los paquetes que vuelan
/// una ruta en el explorador de vuelos (Oleada 16) y la revalidación de un itinerario de IA guardado.
/// La suspensión se aplica al consultar —los productos conservan su propio estado—, así que cada camino
/// nuevo tiene que filtrar por la empresa, y la reactivación tiene que devolverlo todo.
/// </summary>
[Collection(ApiCollection.Name)]
public class SuspendedCompanyLeaksTests(TurisClickApiFactory factory)
{
    private readonly TurisClickApiFactory _factory = factory;

    private static DateOnly AvailabilityDate => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await LoginAsAdminAsync(client));
        return client;
    }

    private static async Task<(Guid Id, string Name)> CreateCityAsync(HttpClient adminClient, string tag)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var country = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = $"Pais{tag}{suffix}", type = "COUNTRY" }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var region = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = $"Region{tag}{suffix}", type = "REGION", parentId = country!.Id }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var cityName = $"Ciudad{tag}{suffix}";
        var city = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = cityName, type = "CITY", parentId = region!.Id }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        return (city!.Id, cityName);
    }

    private async Task<(HttpClient Provider, Guid CompanyId)> CreateProviderAsync(string emailPrefix)
    {
        var provider = _factory.CreateClient();
        var registration = await RegisterApprovedProviderAsync(provider, _factory.CreateClient(), emailPrefix);
        UseBearerToken(provider, registration.AccessToken);
        return (provider, registration.Company.Id);
    }

    private static async Task SetCompanySuspendedAsync(HttpClient adminClient, Guid companyId, bool suspended)
    {
        var action = suspended ? "suspend" : "reactivate";
        var response = await adminClient.PostAsync($"/api/admin/companies/{companyId}/{action}", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- POST /api/flights/search: paquetes que vuelan la ruta ----

    private static object SearchBody() => new
    {
        originIata = "VVI",
        destinationIata = "AMM",
        departureDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(20)).ToString("yyyy-MM-dd"),
        passengers = 1,
        cabinClass = "ECONOMY",
    };

    private static async Task<List<Guid>> RoutePackageIdsAsync(HttpClient tourist)
    {
        var response = await tourist.PostAsJsonAsync("/api/flights/search", SearchBody());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SearchFlightsResponse>(JsonOptions);
        return result!.Packages.Select(p => p.Id).ToList();
    }

    [Fact]
    public async Task FlightSearch_HidesRoutePackagesOfASuspendedCompany_AndReactivationRestoresThem()
    {
        var adminClient = await AdminClientAsync();
        var (cityId, _) = await CreateCityAsync(adminClient, "Vuelo");
        var (provider, companyId) = await CreateProviderAsync("susp-flight");

        var package = await (await provider.PostAsJsonAsync("/api/packages", new
        {
            title = $"Paquete Ruta Suspendida {Guid.NewGuid():N}",
            description = "Siete días por Jordania con guía local y traslados incluidos.",
            destinationId = cityId,
            categoryIds = Array.Empty<Guid>(),
            durationDays = 7,
            price = 1090m,
            currency = "USD",
            items = new[] { new { dayNumber = 1, sortOrder = 1, kind = "DESCRIPTIVE", title = "Llegada y traslado" } },
            images = Array.Empty<object>(),
        })).Content.ReadFromJsonAsync<PackageResponse>(JsonOptions);

        await provider.PostAsJsonAsync($"/api/packages/{package!.Id}/availability",
            new { departureDate = AvailabilityDate.AddDays(30).ToString("yyyy-MM-dd"), totalSlots = 10 });
        Assert.Equal(HttpStatusCode.OK, (await provider.PostAsync($"/api/packages/{package.Id}/publish", null)).StatusCode);

        var rule = await provider.PutAsJsonAsync($"/api/packages/{package.Id}/flight-rule", new
        {
            destinationIata = "AMM",
            allowedOriginIatas = new[] { "VVI", "LPB" },
            cabinClass = "ECONOMY",
            outboundOffsetDays = -1,
            inboundOffsetDays = 0,
            roundTrip = true,
        });
        Assert.Equal(HttpStatusCode.OK, rule.StatusCode);

        var tourist = _factory.CreateClient();
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, "susp-flight-t"));

        // Por id y no por conteo: la base de tests es compartida y otros casos publican paquetes a AMM.
        Assert.Contains(package.Id, await RoutePackageIdsAsync(tourist));

        await SetCompanySuspendedAsync(adminClient, companyId, suspended: true);
        Assert.DoesNotContain(package.Id, await RoutePackageIdsAsync(tourist));

        await SetCompanySuspendedAsync(adminClient, companyId, suspended: false);
        Assert.Contains(package.Id, await RoutePackageIdsAsync(tourist));
    }

    // ---- Itinerario de IA guardado ----

    [Fact]
    public async Task SavedItinerary_FromASuspendedCompany_IsNotBookable_AndReactivationRestoresIt()
    {
        var adminClient = await AdminClientAsync();
        var (cityId, cityName) = await CreateCityAsync(adminClient, "Itin");
        var (provider, companyId) = await CreateProviderAsync("susp-itin");

        var experience = await (await provider.PostAsJsonAsync("/api/experiences", new
        {
            title = $"Tour Suspendido {Guid.NewGuid().ToString("N")[..8]}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = cityId,
            categoryIds = Array.Empty<Guid>(),
            price = 40,
            currency = "USD"
        })).Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);
        await provider.PostAsJsonAsync($"/api/experiences/{experience!.Id}/availability",
            new { date = AvailabilityDate, totalSlots = 10 });
        Assert.Equal(HttpStatusCode.OK, (await provider.PostAsync($"/api/experiences/{experience.Id}/publish", null)).StatusCode);

        var tourist = _factory.CreateClient();
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, "susp-itin-t"));
        var conversation = await (await tourist.PostAsync("/api/ai/conversations", null))
            .Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions);
        var generated = await (await tourist.PostAsJsonAsync($"/api/ai/conversations/{conversation!.Id}/messages",
            new { content = $"Quiero ir a {cityName} del {AvailabilityDate.AddDays(-2):yyyy-MM-dd} al {AvailabilityDate.AddDays(2):yyyy-MM-dd}, somos 2 personas." }))
            .Content.ReadFromJsonAsync<SendMessageResponse>(JsonOptions);
        var itinerary = generated!.Itinerary;
        Assert.NotNull(itinerary);
        Assert.Contains(itinerary!.Items, i => i.ExperienceId == experience.Id);
        Assert.Equal(HttpStatusCode.OK, (await tourist.PostAsync($"/api/ai/itineraries/{itinerary.Id}/save", null)).StatusCode);

        async Task<ItineraryResponse> ResumeAsync() =>
            (await (await tourist.GetAsync($"/api/ai/itineraries/{itinerary.Id}"))
                .Content.ReadFromJsonAsync<ItineraryResponse>(JsonOptions))!;

        await SetCompanySuspendedAsync(adminClient, companyId, suspended: true);

        var suspended = await ResumeAsync();
        Assert.False(suspended.IsStillBookable);
        var affected = Assert.Single(suspended.Items, i => i.ExperienceId == experience.Id);
        Assert.False(affected.IsStillAvailable);
        Assert.Equal("UNPUBLISHED", affected.AvailabilityState);
        Assert.Contains(suspended.Warnings, w => w.Contains("suspendido", StringComparison.OrdinalIgnoreCase));

        await SetCompanySuspendedAsync(adminClient, companyId, suspended: false);

        var restored = await ResumeAsync();
        Assert.True(Assert.Single(restored.Items, i => i.ExperienceId == experience.Id).IsStillAvailable);
        Assert.DoesNotContain(restored.Warnings, w => w.Contains("suspendido", StringComparison.OrdinalIgnoreCase));
    }
}
