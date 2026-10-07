using System.Net;
using System.Net.Http.Json;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Modules.Packages.Dtos;
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

    /// <summary>
    /// Una ciudad puede vender sólo paquetes de varios días y ninguna experiencia suelta. Con un único
    /// contador de experiencias, la app no podía distinguir eso de "acá no hay nada que reservar", y
    /// terminaba ofreciendo ciudades vacías en la portada.
    /// </summary>
    [Fact]
    public async Task List_CountsPublishedPackagesSeparatelyFromExperiences()
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
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), "pkgdest");
        UseBearerToken(providerClient, provider.AccessToken);

        // Un paquete con un solo ítem descriptivo: alcanza para publicarlo y no crea ninguna experiencia.
        var package = await (await providerClient.PostAsJsonAsync("/api/packages", new
        {
            title = $"Paquete-{suffix}",
            description = "Descripción suficientemente larga para pasar la validación del paquete.",
            destinationId = city!.Id,
            categoryIds = Array.Empty<Guid>(),
            durationDays = 2,
            price = 500m,
            currency = "BOB",
            items = new[] { new { dayNumber = 1, sortOrder = 1, kind = "DESCRIPTIVE", title = "Traslado", description = "Traslado desde el aeropuerto." } },
        })).Content.ReadFromJsonAsync<PackageResponse>(JsonOptions);

        await providerClient.PostAsJsonAsync($"/api/packages/{package!.Id}/availability",
            new { departureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(9)), totalSlots = 6 });
        var publish = await providerClient.PostAsync($"/api/packages/{package.Id}/publish", null);
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);

        var anonymousClient = _factory.CreateClient();
        var body = await (await anonymousClient.GetAsync("/api/destinations?type=CITY"))
            .Content.ReadFromJsonAsync<List<PublicDestinationResponse>>(JsonOptions);
        var cityEntry = body!.Single(d => d.Id == city.Id);

        Assert.Equal(1, cityEntry.PublishedPackageCount);
        Assert.Equal(0, cityEntry.PublishedExperienceCount);

        // La regla 10 del dominio vale igual para paquetes: sólo las ciudades referencian producto.
        var regionBody = await (await anonymousClient.GetAsync($"/api/destinations/{region.Id}"))
            .Content.ReadFromJsonAsync<PublicDestinationResponse>(JsonOptions);
        Assert.Equal(0, regionBody!.PublishedPackageCount);
    }

    [Fact]
    public async Task List_DoesNotRequireAuthentication()
    {
        var anonymousClient = _factory.CreateClient();

        var response = await anonymousClient.GetAsync("/api/destinations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
