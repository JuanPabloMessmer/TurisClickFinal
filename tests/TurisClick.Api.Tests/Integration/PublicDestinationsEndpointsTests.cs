using System.Net;
using System.Net.Http.Json;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Experiences.Dtos;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>UC-T-03 — Explorar destinos (público, con conteo de experiencias PUBLISHED por nodo).</summary>
[Collection(ApiCollection.Name)]
public class PublicDestinationsEndpointsTests
{
    private readonly TurisClickApiFactory _factory;

    public PublicDestinationsEndpointsTests(TurisClickApiFactory factory) => _factory = factory;

    [Fact]
    public async Task List_CountsOnlyPublishedExperiencesAtCityLevel()
    {
        var adminClient = _factory.CreateClient();
        UseBearerToken(adminClient, await LoginAsAdminAsync(adminClient));
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var country = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = $"País-{suffix}", type = "COUNTRY" }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var region = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = $"Región-{suffix}", type = "REGION", parentId = country!.Id }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var city = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = $"Ciudad-{suffix}", type = "CITY", parentId = region!.Id }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);

        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), "pubdest");
        UseBearerToken(providerClient, provider.AccessToken);
        var experience = await (await providerClient.PostAsJsonAsync("/api/experiences", new
        {
            title = $"Tour-{suffix}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = city!.Id,
            categoryIds = Array.Empty<Guid>(),
            price = 30m,
            currency = "USD"
        })).Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);

        await providerClient.PostAsJsonAsync($"/api/experiences/{experience!.Id}/availability",
            new { date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)), totalSlots = 5 });
        await providerClient.PostAsync($"/api/experiences/{experience.Id}/publish", null);

        var anonymousClient = _factory.CreateClient();
        var response = await anonymousClient.GetAsync($"/api/destinations?type=CITY");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<PublicDestinationResponse>>(JsonOptions);
        var cityEntry = body!.Single(d => d.Id == city.Id);
        Assert.Equal(1, cityEntry.PublishedExperienceCount);

        var regionResponse = await anonymousClient.GetAsync($"/api/destinations/{region!.Id}");
        var regionBody = await regionResponse.Content.ReadFromJsonAsync<PublicDestinationResponse>(JsonOptions);
        Assert.Equal(0, regionBody!.PublishedExperienceCount);
    }

    [Fact]
    public async Task List_DoesNotRequireAuthentication()
    {
        var anonymousClient = _factory.CreateClient();

        var response = await anonymousClient.GetAsync("/api/destinations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
