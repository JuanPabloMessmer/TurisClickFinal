using System.Net;
using System.Net.Http.Json;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Modules.Packages.Dtos;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>UC-P-11 — Definir disponibilidad de un paquete.</summary>
[Collection(ApiCollection.Name)]
public class PackageAvailabilityEndpointsTests
{
    private readonly TurisClickApiFactory _factory;

    public PackageAvailabilityEndpointsTests(TurisClickApiFactory factory) => _factory = factory;

    private async Task<(HttpClient ProviderClient, PackageResponse Package)> CreateOwnedPackageAsync(string emailPrefix)
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
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), emailPrefix);
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

        var package = await (await providerClient.PostAsJsonAsync("/api/packages", new
        {
            title = $"Paquete-{suffix}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = city.Id,
            categoryIds = Array.Empty<Guid>(),
            durationDays = 2,
            price = 200m,
            currency = "USD",
            items = new object[] { new { dayNumber = 1, sortOrder = 0, kind = "EXPERIENCE_REFERENCE", experienceId = experience!.Id } },
            images = Array.Empty<object>()
        })).Content.ReadFromJsonAsync<PackageResponse>(JsonOptions);

        return (providerClient, package!);
    }

    [Fact]
    public async Task Create_AsOwner_Returns201()
    {
        var (providerClient, package) = await CreateOwnedPackageAsync("pkgavail-create");

        var response = await providerClient.PostAsJsonAsync($"/api/packages/{package.Id}/availability", new
        {
            departureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)),
            totalSlots = 15
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PackageAvailabilityResponse>(JsonOptions);
        Assert.Equal(15, body!.AvailableSlots);
    }

    [Fact]
    public async Task Create_DuplicateDeparture_Returns409()
    {
        var (providerClient, package) = await CreateOwnedPackageAsync("pkgavail-dup");
        var payload = new { departureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)), totalSlots = 5 };

        await providerClient.PostAsJsonAsync($"/api/packages/{package.Id}/availability", payload);
        var second = await providerClient.PostAsJsonAsync($"/api/packages/{package.Id}/availability", payload);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Create_PastDate_Returns400()
    {
        var (providerClient, package) = await CreateOwnedPackageAsync("pkgavail-past");

        var response = await providerClient.PostAsJsonAsync($"/api/packages/{package.Id}/availability", new
        {
            departureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            totalSlots = 5
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_AsAnotherProvider_Returns403()
    {
        var (_, package) = await CreateOwnedPackageAsync("pkgavail-owner");

        var attackerClient = _factory.CreateClient();
        var attacker = await RegisterApprovedProviderAsync(attackerClient, _factory.CreateClient(), "pkgavail-attacker");
        UseBearerToken(attackerClient, attacker.AccessToken);

        var response = await attackerClient.PostAsJsonAsync($"/api/packages/{package.Id}/availability", new
        {
            departureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)),
            totalSlots = 999
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListPublic_PackageStillDraft_Returns404()
    {
        var (_, package) = await CreateOwnedPackageAsync("pkgavail-public-draft");

        var anonymousClient = _factory.CreateClient();
        var response = await anonymousClient.GetAsync($"/api/packages/{package.Id}/availability");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListPublic_PublishedPackage_ReturnsOnlyBookableSlots()
    {
        var (providerClient, package) = await CreateOwnedPackageAsync("pkgavail-public-ok");

        await providerClient.PostAsJsonAsync($"/api/packages/{package.Id}/availability",
            new { departureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(9)), totalSlots = 10 });
        await providerClient.PostAsync($"/api/packages/{package.Id}/publish", null);

        var anonymousClient = _factory.CreateClient();
        var response = await anonymousClient.GetAsync($"/api/packages/{package.Id}/availability");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<PackageAvailabilityResponse>>(JsonOptions);
        Assert.Single(body!);
        Assert.Equal(10, body![0].AvailableSlots);
    }

    [Fact]
    public async Task ListOwned_ReturnsSlotsRegardlessOfPackageStatus()
    {
        var (providerClient, package) = await CreateOwnedPackageAsync("pkgavail-owned-list");

        await providerClient.PostAsJsonAsync($"/api/packages/{package.Id}/availability",
            new { departureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(9)), totalSlots = 10 });

        var response = await providerClient.GetAsync($"/api/packages/mine/{package.Id}/availability");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<PackageAvailabilityResponse>>(JsonOptions);
        Assert.Single(body!); // el paquete sigue en DRAFT y el slot igual aparece acá
    }
}
