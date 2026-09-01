using System.Net;
using System.Net.Http.Json;
using TurisClick.Api.Modules.Ai.Dtos;
using TurisClick.Api.Modules.Categories.Dtos;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Modules.Packages.Dtos;
using TurisClick.Api.Shared.Responses;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// UC-T-12/13/14, UC-AI-01/02/03/04 — usa el proveedor "Deterministic" (default de Testing, ver
/// appsettings.json), nunca Ollama real — sección 18/19 de la sesión: los tests no dependen de un LLM
/// no determinístico.
/// </summary>
[Collection(ApiCollection.Name)]
public class AiEndpointsTests
{
    private readonly TurisClickApiFactory _factory;

    public AiEndpointsTests(TurisClickApiFactory factory) => _factory = factory;

    /// <summary>Fecha relativa (no hardcodeada) usada tanto para sembrar disponibilidad como para el rango de fechas del mensaje de prueba — evita que los tests se pudran con el paso del tiempo.</summary>
    private static DateOnly AvailabilityDate => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));

    private async Task<(Guid CityId, string CityName, Guid CategoryId, string CategoryName, Guid ExperienceId, Guid PackageId)>
        SeedCatalogAsync(string emailPrefix)
    {
        var adminClient = _factory.CreateClient();
        UseBearerToken(adminClient, await LoginAsAdminAsync(adminClient));
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var country = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = $"PaisAi{suffix}", type = "COUNTRY" }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var region = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = $"RegionAi{suffix}", type = "REGION", parentId = country!.Id }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var cityName = $"CiudadAi{suffix}";
        var city = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = cityName, type = "CITY", parentId = region!.Id }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);

        var categoryName = $"InteresAi{suffix}";
        var category = await (await adminClient.PostAsJsonAsync("/api/admin/categories", new { name = categoryName }))
            .Content.ReadFromJsonAsync<CategoryResponse>(JsonOptions);

        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), emailPrefix);
        UseBearerToken(providerClient, provider.AccessToken);

        var experience = await (await providerClient.PostAsJsonAsync("/api/experiences", new
        {
            title = $"Tour Ai {suffix}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = city!.Id,
            categoryIds = new[] { category!.Id },
            price = 40m,
            currency = "USD"
        })).Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);

        // El retrieval filtra por fecha — la disponibilidad tiene que caer dentro del rango que el
        // mensaje de prueba va a pedir (ver AvailabilityDate más abajo, misma fórmula relativa).
        await providerClient.PostAsJsonAsync($"/api/experiences/{experience!.Id}/availability", new
        {
            date = AvailabilityDate,
            totalSlots = 10
        });
        await providerClient.PostAsync($"/api/experiences/{experience.Id}/publish", null);

        var package = await (await providerClient.PostAsJsonAsync("/api/packages", new
        {
            title = $"Paquete Ai {suffix}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = city.Id,
            categoryIds = new[] { category.Id },
            durationDays = 3,
            price = 300m,
            currency = "USD",
            items = new object[] { new { dayNumber = 1, sortOrder = 0, kind = "EXPERIENCE_REFERENCE", experienceId = experience.Id } },
            images = Array.Empty<object>()
        })).Content.ReadFromJsonAsync<PackageResponse>(JsonOptions);

        await providerClient.PostAsJsonAsync($"/api/packages/{package!.Id}/availability", new
        {
            departureDate = AvailabilityDate,
            totalSlots = 10
        });
        await providerClient.PostAsync($"/api/packages/{package.Id}/publish", null);

        return (city.Id, cityName, category.Id, categoryName, experience.Id, package.Id);
    }

    [Fact]
    public async Task CreateConversation_ReturnsActiveEmptyConversation()
    {
        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "ai-create"));

        var response = await touristClient.PostAsync("/api/ai/conversations", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions);
        Assert.Equal("ACTIVE", body!.Status);
        Assert.Empty(body.Messages);
        Assert.Null(body.Preferences.PreferredDestinationId);
    }

    [Fact]
    public async Task SendMessage_IncompleteInformation_ReturnsClarificationNeeded()
    {
        var (_, cityName, _, _, _, _) = await SeedCatalogAsync("ai-clarify");
        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "ai-clarify-t"));
        var conversation = await (await touristClient.PostAsync("/api/ai/conversations", null)).Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions);

        var response = await touristClient.PostAsJsonAsync($"/api/ai/conversations/{conversation!.Id}/messages",
            new { content = $"Quiero ir a {cityName}." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SendMessageResponse>(JsonOptions);
        Assert.True(body!.ClarificationNeeded);
        Assert.Contains(body.MissingInformation, m => m.Contains("viajeros"));
        Assert.Null(body.Itinerary);
        Assert.Equal(cityName, body.ParsedPreferences.PreferredDestinationName);
    }

    [Fact]
    public async Task SendMessage_AccumulatesPreferencesAcrossTurns_ThenGeneratesItinerary()
    {
        var (cityId, cityName, categoryId, categoryName, experienceId, packageId) = await SeedCatalogAsync("ai-accum");
        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "ai-accum-t"));
        var conversation = await (await touristClient.PostAsync("/api/ai/conversations", null)).Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions);

        // Turno 1: solo destino.
        var first = await (await touristClient.PostAsJsonAsync($"/api/ai/conversations/{conversation!.Id}/messages",
            new { content = $"Quiero ir a {cityName}." })).Content.ReadFromJsonAsync<SendMessageResponse>(JsonOptions);
        Assert.True(first!.ClarificationNeeded);

        // Turno 2: fechas y viajeros — el destino del turno 1 se conserva (se fusiona, no se resetea).
        var startDate = AvailabilityDate.AddDays(-2);
        var endDate = AvailabilityDate.AddDays(2);
        var second = await touristClient.PostAsJsonAsync($"/api/ai/conversations/{conversation.Id}/messages",
            new { content = $"Del {startDate:yyyy-MM-dd} al {endDate:yyyy-MM-dd}, somos 2 personas. Nos gusta {categoryName}." });

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<SendMessageResponse>(JsonOptions);
        Assert.False(body!.ClarificationNeeded);
        Assert.Equal(cityName, body.ParsedPreferences.PreferredDestinationName); // se conservó del turno anterior
        Assert.Equal(2, body.ParsedPreferences.TravelersCount);
        Assert.NotNull(body.Itinerary);
        Assert.NotEmpty(body.Itinerary!.Items);

        // Todo lo que aparece en el itinerario debe referenciar productos reales de este test.
        Assert.All(body.Itinerary.Items, item =>
        {
            Assert.True(item.ExperienceId == experienceId || item.PackageId == packageId);
        });

        // UC-T-14: la propuesta se puede recuperar después.
        var fetched = await touristClient.GetAsync($"/api/ai/conversations/{conversation.Id}/itinerary");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        var fetchedBody = await fetched.Content.ReadFromJsonAsync<ItineraryResponse>(JsonOptions);
        Assert.Equal(body.Itinerary.Id, fetchedBody!.Id);

        var byId = await touristClient.GetAsync($"/api/ai/itineraries/{body.Itinerary.Id}");
        Assert.Equal(HttpStatusCode.OK, byId.StatusCode);
    }

    [Fact]
    public async Task SendMessage_NoPublishedCandidates_ReturnsNoItineraryWithWarning()
    {
        var adminClient = _factory.CreateClient();
        UseBearerToken(adminClient, await LoginAsAdminAsync(adminClient));
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var country = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = $"PaisVacio{suffix}", type = "COUNTRY" }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var region = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = $"RegionVacia{suffix}", type = "REGION", parentId = country!.Id }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var cityName = $"CiudadVacia{suffix}";
        await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = cityName, type = "CITY", parentId = region!.Id });

        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "ai-empty-t"));
        var conversation = await (await touristClient.PostAsync("/api/ai/conversations", null)).Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions);

        var response = await touristClient.PostAsJsonAsync($"/api/ai/conversations/{conversation!.Id}/messages",
            new { content = $"Quiero ir a {cityName} del {DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)):yyyy-MM-dd} al {DateOnly.FromDateTime(DateTime.UtcNow.AddDays(32)):yyyy-MM-dd}, somos 2 personas." });

        var body = await response.Content.ReadFromJsonAsync<SendMessageResponse>(JsonOptions);
        Assert.False(body!.ClarificationNeeded);
        Assert.Null(body.Itinerary);
        Assert.NotEmpty(body.Warnings);
    }

    [Fact]
    public async Task Retrieval_IgnoresUnpublishedAndUnavailableExperiences()
    {
        var adminClient = _factory.CreateClient();
        UseBearerToken(adminClient, await LoginAsAdminAsync(adminClient));
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var country = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = $"PaisDraft{suffix}", type = "COUNTRY" }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var region = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = $"RegionDraft{suffix}", type = "REGION", parentId = country!.Id }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var cityName = $"CiudadDraft{suffix}";
        var city = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = cityName, type = "CITY", parentId = region!.Id }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);

        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), $"ai-draft-{suffix}");
        UseBearerToken(providerClient, provider.AccessToken);

        // Experience DRAFT (nunca publicada) — no debería aparecer nunca como candidata.
        var draftExperience = await (await providerClient.PostAsJsonAsync("/api/experiences", new
        {
            title = $"Draft {suffix}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = city!.Id,
            categoryIds = Array.Empty<Guid>(),
            price = 10m,
            currency = "USD"
        })).Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);
        await providerClient.PostAsJsonAsync($"/api/experiences/{draftExperience!.Id}/availability", new
        {
            date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            totalSlots = 10
        });
        // Nunca se publica.

        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, $"ai-draft-t-{suffix}"));
        var conversation = await (await touristClient.PostAsync("/api/ai/conversations", null)).Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions);

        var response = await touristClient.PostAsJsonAsync($"/api/ai/conversations/{conversation!.Id}/messages",
            new { content = $"Quiero ir a {cityName} del {DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)):yyyy-MM-dd} al {DateOnly.FromDateTime(DateTime.UtcNow.AddDays(32)):yyyy-MM-dd}, somos 2 personas." });

        var body = await response.Content.ReadFromJsonAsync<SendMessageResponse>(JsonOptions);
        // Sin candidatos publicados: no debería haber itinerario (la Experience DRAFT nunca cuenta).
        Assert.Null(body!.Itinerary);
    }

    [Fact]
    public async Task GetById_AsAnotherTourist_Returns403()
    {
        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "ai-owner"));
        var conversation = await (await touristClient.PostAsync("/api/ai/conversations", null)).Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions);

        var attackerClient = _factory.CreateClient();
        UseBearerToken(attackerClient, await RegisterAndLoginTouristAsync(attackerClient, "ai-attacker"));

        var getResponse = await attackerClient.GetAsync($"/api/ai/conversations/{conversation!.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, getResponse.StatusCode);

        var messageResponse = await attackerClient.PostAsJsonAsync($"/api/ai/conversations/{conversation.Id}/messages", new { content = "Hola" });
        Assert.Equal(HttpStatusCode.Forbidden, messageResponse.StatusCode);
    }

    [Fact]
    public async Task ListMine_ReturnsOnlyOwnConversations()
    {
        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "ai-list-mine"));
        await touristClient.PostAsync("/api/ai/conversations", null);

        var otherClient = _factory.CreateClient();
        UseBearerToken(otherClient, await RegisterAndLoginTouristAsync(otherClient, "ai-list-other"));
        await otherClient.PostAsync("/api/ai/conversations", null);

        var response = await touristClient.GetAsync("/api/ai/conversations/me");
        var body = await response.Content.ReadFromJsonAsync<PagedResult<ConversationSummaryResponse>>(JsonOptions);

        Assert.Single(body!.Items);
    }

    [Fact]
    public async Task Create_WithoutAuth_Returns401()
    {
        var anonymousClient = _factory.CreateClient();

        var response = await anonymousClient.PostAsync("/api/ai/conversations", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_AsProvider_Returns403()
    {
        var providerClient = _factory.CreateClient();
        var provider = await RegisterProviderAsync(providerClient, "ai-provider-role");
        UseBearerToken(providerClient, provider.AccessToken);

        var response = await providerClient.PostAsync("/api/ai/conversations", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
