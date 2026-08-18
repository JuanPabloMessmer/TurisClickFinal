using System.Net;
using System.Net.Http.Json;
using TurisClick.Api.Modules.Destinations.Dtos;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>UC-A-04 — Gestionar destinos. Exclusivo de ADMIN.</summary>
[Collection(ApiCollection.Name)]
public class DestinationsEndpointsTests
{
    private readonly TurisClickApiFactory _factory;

    public DestinationsEndpointsTests(TurisClickApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_AsAdmin_FullHierarchy_Succeeds()
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await LoginAsAdminAsync(client));
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var countryResponse = await client.PostAsJsonAsync("/api/admin/destinations", new { name = $"País-{suffix}", type = "COUNTRY" });
        Assert.Equal(HttpStatusCode.Created, countryResponse.StatusCode);
        var country = await countryResponse.Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);

        var regionResponse = await client.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"Región-{suffix}", type = "REGION", parentId = country!.Id });
        Assert.Equal(HttpStatusCode.Created, regionResponse.StatusCode);
        var region = await regionResponse.Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);

        var cityResponse = await client.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"Ciudad-{suffix}", type = "CITY", parentId = region!.Id });
        Assert.Equal(HttpStatusCode.Created, cityResponse.StatusCode);
        var city = await cityResponse.Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);

        Assert.Equal(region.Id, city!.ParentId);
        Assert.Equal(region.Name, city.ParentName);
    }

    [Fact]
    public async Task Create_RegionWithoutParent_Returns400()
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await LoginAsAdminAsync(client));

        var response = await client.PostAsJsonAsync("/api/admin/destinations", new { name = "Región huérfana", type = "REGION" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_DestinationWithChildren_Returns409()
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await LoginAsAdminAsync(client));
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var countryResponse = await client.PostAsJsonAsync("/api/admin/destinations", new { name = $"País-{suffix}", type = "COUNTRY" });
        var country = await countryResponse.Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        await client.PostAsJsonAsync("/api/admin/destinations", new { name = $"Región-{suffix}", type = "REGION", parentId = country!.Id });

        var deleteResponse = await client.DeleteAsync($"/api/admin/destinations/{country.Id}");

        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task List_AsTourist_Returns403()
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await RegisterAndLoginTouristAsync(client, "dest-tourist"));

        var response = await client.GetAsync("/api/admin/destinations");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_AsProvider_Returns403()
    {
        var client = _factory.CreateClient();
        var provider = await RegisterProviderAsync(client, "dest-provider");
        UseBearerToken(client, provider.AccessToken);

        var response = await client.GetAsync("/api/admin/destinations");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/admin/destinations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
