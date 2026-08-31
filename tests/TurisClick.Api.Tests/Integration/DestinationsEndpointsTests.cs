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
    public async Task Delete_DestinationWithoutReferences_Returns204()
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await LoginAsAdminAsync(client));
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var country = await (await client.PostAsJsonAsync("/api/admin/destinations", new { name = $"País-{suffix}", type = "COUNTRY" }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var region = await (await client.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"Región-{suffix}", type = "REGION", parentId = country!.Id })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var city = await (await client.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"Ciudad-{suffix}", type = "CITY", parentId = region!.Id })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);

        var response = await client.DeleteAsync($"/api/admin/destinations/{city!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>
    /// UC-A-04 — antes de este fix, borrar un destino en uso violaba la FK Restrict de
    /// experiences.destination_id en Postgres y el cliente recibía un 500 genérico de EF
    /// ("An error occurred while saving the entity changes..."). Ahora es un 409 de dominio explícito.
    /// </summary>
    [Fact]
    public async Task Delete_DestinationUsedByExperience_Returns409WithDomainMessage_NotGenericEfError()
    {
        var adminClient = _factory.CreateClient();
        UseBearerToken(adminClient, await LoginAsAdminAsync(adminClient));
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var country = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = $"País-{suffix}", type = "COUNTRY" }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var region = await (await adminClient.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"Región-{suffix}", type = "REGION", parentId = country!.Id })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var city = await (await adminClient.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"Ciudad-{suffix}", type = "CITY", parentId = region!.Id })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);

        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), $"dest-del-exp-{suffix}");
        UseBearerToken(providerClient, provider.AccessToken);
        await providerClient.PostAsJsonAsync("/api/experiences", new
        {
            title = $"Tour-{suffix}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = city!.Id,
            categoryIds = Array.Empty<Guid>(),
            price = 10m,
            currency = "USD"
        });

        var response = await adminClient.DeleteAsync($"/api/admin/destinations/{city.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("experiencias o paquetes", body);
        Assert.DoesNotContain("DbUpdateException", body);
        Assert.DoesNotContain("entity changes", body);
        Assert.DoesNotContain("23503", body);
    }

    /// <summary>Igual que la referenciada por Experience, pero contra un Package existente (Oleada 4).</summary>
    [Fact]
    public async Task Delete_DestinationUsedByPackage_Returns409WithDomainMessage_NotGenericEfError()
    {
        var adminClient = _factory.CreateClient();
        UseBearerToken(adminClient, await LoginAsAdminAsync(adminClient));
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var country = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = $"País-{suffix}", type = "COUNTRY" }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var region = await (await adminClient.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"Región-{suffix}", type = "REGION", parentId = country!.Id })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var city = await (await adminClient.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"Ciudad-{suffix}", type = "CITY", parentId = region!.Id })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);

        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), $"dest-del-pkg-{suffix}");
        UseBearerToken(providerClient, provider.AccessToken);
        await providerClient.PostAsJsonAsync("/api/packages", new
        {
            title = $"Paquete-{suffix}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = city!.Id,
            categoryIds = Array.Empty<Guid>(),
            durationDays = 1,
            price = 100m,
            currency = "USD",
            items = Array.Empty<object>(),
            images = Array.Empty<object>()
        });

        var response = await adminClient.DeleteAsync($"/api/admin/destinations/{city.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("experiencias o paquetes", body);
        Assert.DoesNotContain("DbUpdateException", body);
        Assert.DoesNotContain("entity changes", body);
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
