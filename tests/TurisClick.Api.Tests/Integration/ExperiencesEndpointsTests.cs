using System.Net;
using System.Net.Http.Json;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Shared.Responses;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>UC-P-04/05/06, UC-T-04/05 — CRUD, publicación y catálogo público de Experience.</summary>
[Collection(ApiCollection.Name)]
public class ExperiencesEndpointsTests
{
    private readonly TurisClickApiFactory _factory;

    public ExperiencesEndpointsTests(TurisClickApiFactory factory) => _factory = factory;

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

    private static object ValidPayload(Guid destinationId, string title = "City Tour La Paz") => new
    {
        title,
        description = "Recorrido guiado por el centro histórico y los principales miradores.",
        destinationId,
        categoryIds = Array.Empty<Guid>(),
        price = 45.5m,
        currency = "USD",
        durationLabel = "4 horas"
    };

    [Fact]
    public async Task Create_AsApprovedProvider_Returns201InDraft()
    {
        var adminClient = _factory.CreateClient();
        var destinationId = await CreateCityDestinationAsync(adminClient);

        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, adminClient, "exp-create");
        UseBearerToken(providerClient, provider.AccessToken);

        var response = await providerClient.PostAsJsonAsync("/api/experiences", ValidPayload(destinationId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);
        Assert.Equal("DRAFT", body!.Status);
        Assert.Equal(provider.Company.Id, body.CompanyId);
    }

    [Fact]
    public async Task Create_AsUnapprovedProvider_Returns403()
    {
        var adminClient = _factory.CreateClient();
        var destinationId = await CreateCityDestinationAsync(adminClient);

        var providerClient = _factory.CreateClient();
        var provider = await RegisterProviderAsync(providerClient, "exp-unapproved"); // sin aprobar
        UseBearerToken(providerClient, provider.AccessToken);

        var response = await providerClient.PostAsJsonAsync("/api/experiences", ValidPayload(destinationId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_AsTourist_Returns403()
    {
        var adminClient = _factory.CreateClient();
        var destinationId = await CreateCityDestinationAsync(adminClient);

        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "exp-tourist"));

        var response = await touristClient.PostAsJsonAsync("/api/experiences", ValidPayload(destinationId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_DestinationIsNotCity_Returns400()
    {
        var adminClient = _factory.CreateClient();
        UseBearerToken(adminClient, await LoginAsAdminAsync(adminClient));
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var country = await (await adminClient.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"País-{suffix}", type = "COUNTRY" })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);

        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), "exp-badcity");
        UseBearerToken(providerClient, provider.AccessToken);

        var response = await providerClient.PostAsJsonAsync("/api/experiences", ValidPayload(country!.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_ExperienceOfAnotherCompany_Returns403()
    {
        var adminClient = _factory.CreateClient();
        var destinationId = await CreateCityDestinationAsync(adminClient);

        var ownerClient = _factory.CreateClient();
        var owner = await RegisterApprovedProviderAsync(ownerClient, _factory.CreateClient(), "exp-owner");
        UseBearerToken(ownerClient, owner.AccessToken);
        var created = await (await ownerClient.PostAsJsonAsync("/api/experiences", ValidPayload(destinationId)))
            .Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);

        var attackerClient = _factory.CreateClient();
        var attacker = await RegisterApprovedProviderAsync(attackerClient, _factory.CreateClient(), "exp-attacker");
        UseBearerToken(attackerClient, attacker.AccessToken);

        var response = await attackerClient.PutAsJsonAsync($"/api/experiences/{created!.Id}", ValidPayload(destinationId, "Título hackeado"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Confirma que el ataque no tuvo ningún efecto: el dueño real sigue viendo el título original.
        UseBearerToken(ownerClient, owner.AccessToken);
        var stillOwned = await (await ownerClient.GetAsync($"/api/experiences/mine/{created.Id}"))
            .Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);
        Assert.Equal("City Tour La Paz", stillOwned!.Title);
    }

    [Fact]
    public async Task Publish_WithoutFutureAvailability_Returns409()
    {
        var adminClient = _factory.CreateClient();
        var destinationId = await CreateCityDestinationAsync(adminClient);

        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), "exp-publish-noavail");
        UseBearerToken(providerClient, provider.AccessToken);
        var created = await (await providerClient.PostAsJsonAsync("/api/experiences", ValidPayload(destinationId)))
            .Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);

        var response = await providerClient.PostAsync($"/api/experiences/{created!.Id}/publish", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Publish_WithFutureAvailability_MakesItVisibleInPublicSearchAndDetail()
    {
        var adminClient = _factory.CreateClient();
        var destinationId = await CreateCityDestinationAsync(adminClient);

        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), "exp-publish-ok");
        UseBearerToken(providerClient, provider.AccessToken);
        var created = await (await providerClient.PostAsJsonAsync("/api/experiences", ValidPayload(destinationId, $"Publicada-{Guid.NewGuid():N}")))
            .Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);

        await providerClient.PostAsJsonAsync($"/api/experiences/{created!.Id}/availability", new
        {
            date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            totalSlots = 20
        });

        var publishResponse = await providerClient.PostAsync($"/api/experiences/{created.Id}/publish", null);
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);

        var anonymousClient = _factory.CreateClient();
        var publicDetail = await anonymousClient.GetAsync($"/api/experiences/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, publicDetail.StatusCode);

        var searchResponse = await anonymousClient.GetAsync($"/api/experiences?destinationId={destinationId}");
        var searchBody = await searchResponse.Content.ReadFromJsonAsync<PagedResult<ExperienceSummaryResponse>>(JsonOptions);
        Assert.Contains(searchBody!.Items, e => e.Id == created.Id);
    }

    [Fact]
    public async Task GetPublishedById_ExperienceStillDraft_Returns404ToAnonymousUser()
    {
        var adminClient = _factory.CreateClient();
        var destinationId = await CreateCityDestinationAsync(adminClient);

        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), "exp-draft-hidden");
        UseBearerToken(providerClient, provider.AccessToken);
        var created = await (await providerClient.PostAsJsonAsync("/api/experiences", ValidPayload(destinationId)))
            .Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);

        var anonymousClient = _factory.CreateClient();
        var response = await anonymousClient.GetAsync($"/api/experiences/{created!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListMine_ReturnsOnlyOwnCompanyExperiences()
    {
        var adminClient = _factory.CreateClient();
        var destinationId = await CreateCityDestinationAsync(adminClient);

        var providerAClient = _factory.CreateClient();
        var providerA = await RegisterApprovedProviderAsync(providerAClient, _factory.CreateClient(), "exp-mine-a");
        UseBearerToken(providerAClient, providerA.AccessToken);
        var createdA = await (await providerAClient.PostAsJsonAsync("/api/experiences", ValidPayload(destinationId, $"DeA-{Guid.NewGuid():N}")))
            .Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);

        var providerBClient = _factory.CreateClient();
        var providerB = await RegisterApprovedProviderAsync(providerBClient, _factory.CreateClient(), "exp-mine-b");
        UseBearerToken(providerBClient, providerB.AccessToken);
        await providerBClient.PostAsJsonAsync("/api/experiences", ValidPayload(destinationId, $"DeB-{Guid.NewGuid():N}"));

        var mineResponse = await providerAClient.GetAsync("/api/experiences/mine");
        var mineBody = await mineResponse.Content.ReadFromJsonAsync<PagedResult<ExperienceSummaryResponse>>(JsonOptions);

        Assert.All(mineBody!.Items, item => Assert.True(item.Id == createdA!.Id || item.CompanyName == providerA.Company.Name));
        Assert.Contains(mineBody.Items, item => item.Id == createdA!.Id);
    }
}
