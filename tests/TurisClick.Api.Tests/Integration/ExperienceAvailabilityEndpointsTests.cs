using System.Net;
using System.Net.Http.Json;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Experiences.Dtos;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>UC-P-10 — Definir disponibilidad de una experiencia.</summary>
[Collection(ApiCollection.Name)]
public class ExperienceAvailabilityEndpointsTests
{
    private readonly TurisClickApiFactory _factory;

    public ExperienceAvailabilityEndpointsTests(TurisClickApiFactory factory) => _factory = factory;

    private async Task<(HttpClient ProviderClient, ExperienceResponse Experience)> CreateOwnedExperienceAsync(string emailPrefix)
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

        return (providerClient, experience!);
    }

    [Fact]
    public async Task Create_AsOwner_Returns201()
    {
        var (providerClient, experience) = await CreateOwnedExperienceAsync("avail-create");

        var response = await providerClient.PostAsJsonAsync($"/api/experiences/{experience.Id}/availability", new
        {
            date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            totalSlots = 12
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ExperienceAvailabilityResponse>(JsonOptions);
        Assert.Equal(12, body!.AvailableSlots);
    }

    [Fact]
    public async Task Create_DuplicateSlot_Returns409()
    {
        var (providerClient, experience) = await CreateOwnedExperienceAsync("avail-dup");
        var payload = new { date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), totalSlots = 5 };

        await providerClient.PostAsJsonAsync($"/api/experiences/{experience.Id}/availability", payload);
        var second = await providerClient.PostAsJsonAsync($"/api/experiences/{experience.Id}/availability", payload);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Create_PastDate_Returns400()
    {
        var (providerClient, experience) = await CreateOwnedExperienceAsync("avail-past");

        var response = await providerClient.PostAsJsonAsync($"/api/experiences/{experience.Id}/availability", new
        {
            date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            totalSlots = 5
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_AsAnotherProvider_Returns403()
    {
        var (_, experience) = await CreateOwnedExperienceAsync("avail-owner");

        var attackerClient = _factory.CreateClient();
        var attacker = await RegisterApprovedProviderAsync(attackerClient, _factory.CreateClient(), "avail-attacker");
        UseBearerToken(attackerClient, attacker.AccessToken);

        var response = await attackerClient.PostAsJsonAsync($"/api/experiences/{experience.Id}/availability", new
        {
            date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            totalSlots = 999
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListPublic_ExperienceStillDraft_Returns404()
    {
        var (_, experience) = await CreateOwnedExperienceAsync("avail-public-draft");

        var anonymousClient = _factory.CreateClient();
        var response = await anonymousClient.GetAsync($"/api/experiences/{experience.Id}/availability");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListPublic_PublishedExperience_ReturnsOnlyBookableSlots()
    {
        var (providerClient, experience) = await CreateOwnedExperienceAsync("avail-public-ok");

        await providerClient.PostAsJsonAsync($"/api/experiences/{experience.Id}/availability",
            new { date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)), totalSlots = 10 });
        await providerClient.PostAsync($"/api/experiences/{experience.Id}/publish", null);

        var anonymousClient = _factory.CreateClient();
        var response = await anonymousClient.GetAsync($"/api/experiences/{experience.Id}/availability");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<ExperienceAvailabilityResponse>>(JsonOptions);
        Assert.Single(body!);
        Assert.Equal(10, body![0].AvailableSlots);
    }

    [Fact]
    public async Task ListOwned_ReturnsSlotsRegardlessOfExperienceStatus()
    {
        var (providerClient, experience) = await CreateOwnedExperienceAsync("avail-owned-list");

        await providerClient.PostAsJsonAsync($"/api/experiences/{experience.Id}/availability",
            new { date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)), totalSlots = 10 });

        var response = await providerClient.GetAsync($"/api/experiences/mine/{experience.Id}/availability");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<ExperienceAvailabilityResponse>>(JsonOptions);
        Assert.Single(body!); // la experiencia sigue en DRAFT y el slot igual aparece acá
    }
}
