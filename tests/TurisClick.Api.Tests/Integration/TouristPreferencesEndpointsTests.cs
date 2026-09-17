using System.Net;
using System.Net.Http.Json;
using TurisClick.Api.Modules.Ai.Dtos;
using TurisClick.Api.Modules.Categories.Dtos;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Modules.Preferences.Dtos;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>Onboarding: preferencias persistidas del turista, su edición desde Perfil y su uso por el asistente.</summary>
[Collection(ApiCollection.Name)]
public class TouristPreferencesEndpointsTests
{
    private const string Endpoint = "/api/tourists/me/preferences";
    private readonly TurisClickApiFactory _factory;

    public TouristPreferencesEndpointsTests(TurisClickApiFactory factory) => _factory = factory;

    private async Task<HttpClient> TouristAsync(string prefix)
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await RegisterAndLoginTouristAsync(client, prefix));
        return client;
    }

    private async Task<List<CategoryResponse>> CategoriesAsync(int count)
    {
        var admin = _factory.CreateClient();
        UseBearerToken(admin, await LoginAsAdminAsync(admin));
        var result = new List<CategoryResponse>();
        for (var i = 0; i < count; i++)
        {
            var category = await (await admin.PostAsJsonAsync("/api/admin/categories", new { name = $"Pref-{Guid.NewGuid():N}"[..20] }))
                .Content.ReadFromJsonAsync<CategoryResponse>(JsonOptions);
            result.Add(category!);
        }
        return result;
    }

    [Fact]
    public async Task NewTourist_HasEmptyProfile_AndOnboardingPending()
    {
        var tourist = await TouristAsync("pref-new");

        var body = await tourist.GetFromJsonAsync<TouristPreferencesResponse>(Endpoint, JsonOptions);

        Assert.False(body!.OnboardingCompleted);
        Assert.Empty(body.Categories);
        Assert.Null(body.TravelPace);
    }

    [Fact]
    public async Task CompleteOnboarding_Persists_ThenEditingFromProfileReplacesIt()
    {
        var tourist = await TouristAsync("pref-save");
        var categories = await CategoriesAsync(3);

        var saved = await tourist.PutAsJsonAsync(Endpoint, new
        {
            categoryIds = new[] { categories[0].Id, categories[1].Id },
            travelPace = "balanced",
            travelParty = "COUPLE",
            budgetLevel = "ECONOMY",
            completeOnboarding = true
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var reloaded = await tourist.GetFromJsonAsync<TouristPreferencesResponse>(Endpoint, JsonOptions);
        Assert.True(reloaded!.OnboardingCompleted);
        Assert.Equal(2, reloaded.Categories.Count);
        Assert.Equal("BALANCED", reloaded.TravelPace);
        Assert.Equal("COUPLE", reloaded.TravelParty);
        Assert.Equal("ECONOMY", reloaded.BudgetLevel);
        var completedAt = reloaded.OnboardingCompletedAt;

        // Edición desde Perfil: reemplazo completo, y el onboarding no vuelve a quedar pendiente.
        await tourist.PutAsJsonAsync(Endpoint, new { categoryIds = new[] { categories[2].Id }, travelPace = "INTENSE", completeOnboarding = false });
        var edited = await tourist.GetFromJsonAsync<TouristPreferencesResponse>(Endpoint, JsonOptions);

        Assert.Equal(categories[2].Id, Assert.Single(edited!.Categories).Id);
        Assert.Equal("INTENSE", edited.TravelPace);
        Assert.Null(edited.TravelParty);
        Assert.True(edited.OnboardingCompleted);
        Assert.Equal(completedAt, edited.OnboardingCompletedAt);
    }

    [Fact]
    public async Task SkippingOnboarding_CompletesItWithoutData()
    {
        var tourist = await TouristAsync("pref-skip");

        await tourist.PutAsJsonAsync(Endpoint, new { completeOnboarding = true });

        var body = await tourist.GetFromJsonAsync<TouristPreferencesResponse>(Endpoint, JsonOptions);
        Assert.True(body!.OnboardingCompleted);
        Assert.Empty(body.Categories);
    }

    [Theory]
    [InlineData("{\"travelPace\":\"SLOW\"}")]
    [InlineData("{\"travelParty\":\"COWORKERS\"}")]
    [InlineData("{\"budgetLevel\":\"LUXURY\"}")]
    [InlineData("{\"categoryIds\":[\"6f9619ff-8b86-d011-b42d-00cf4fc964ff\"]}")]
    public async Task InvalidValues_Return400(string json)
    {
        var tourist = await TouristAsync("pref-invalid");

        var response = await tourist.PutAsync(Endpoint, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OnlyTourists_CanUsePreferences()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(Endpoint)).StatusCode);

        var provider = _factory.CreateClient();
        UseBearerToken(provider, (await RegisterApprovedProviderAsync(provider, _factory.CreateClient(), "pref-provider")).AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await provider.GetAsync(Endpoint)).StatusCode);

        var admin = _factory.CreateClient();
        UseBearerToken(admin, await LoginAsAdminAsync(admin));
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync(Endpoint, new { completeOnboarding = true })).StatusCode);
    }

    [Fact]
    public async Task EachTourist_SeesOnlyTheirOwnPreferences()
    {
        var first = await TouristAsync("pref-owner-a");
        var second = await TouristAsync("pref-owner-b");

        await first.PutAsJsonAsync(Endpoint, new { travelParty = "SOLO", completeOnboarding = true });

        var other = await second.GetFromJsonAsync<TouristPreferencesResponse>(Endpoint, JsonOptions);
        Assert.Null(other!.TravelParty);
        Assert.False(other.OnboardingCompleted);
    }

    [Fact]
    public async Task Assistant_UsesSavedPreferences_WithoutRepeatingThem()
    {
        // Catálogo mínimo: una ciudad y una experiencia publicada de la categoría de interés.
        var admin = _factory.CreateClient();
        UseBearerToken(admin, await LoginAsAdminAsync(admin));
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var country = await (await admin.PostAsJsonAsync("/api/admin/destinations", new { name = $"PaisPref{suffix}", type = "COUNTRY" })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var region = await (await admin.PostAsJsonAsync("/api/admin/destinations", new { name = $"RegionPref{suffix}", type = "REGION", parentId = country!.Id })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var cityName = $"CiudadPref{suffix}";
        var city = await (await admin.PostAsJsonAsync("/api/admin/destinations", new { name = cityName, type = "CITY", parentId = region!.Id })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var category = (await CategoriesAsync(1))[0];

        var provider = _factory.CreateClient();
        UseBearerToken(provider, (await RegisterApprovedProviderAsync(provider, _factory.CreateClient(), "pref-ai-provider")).AccessToken);
        var experience = await (await provider.PostAsJsonAsync("/api/experiences", new
        {
            title = $"Tour Pref {suffix}", description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = city!.Id, categoryIds = new[] { category.Id }, price = 120m, currency = "BOB"
        })).Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);
        await provider.PostAsJsonAsync($"/api/experiences/{experience!.Id}/availability", new { date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)), totalSlots = 10 });
        await provider.PostAsync($"/api/experiences/{experience.Id}/publish", null);

        var tourist = await TouristAsync("pref-ai");
        await tourist.PutAsJsonAsync(Endpoint, new { categoryIds = new[] { category.Id }, travelParty = "SOLO", budgetLevel = "ECONOMY", completeOnboarding = true });
        var conversation = await (await tourist.PostAsync("/api/ai/conversations", null)).Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions);

        // No dice intereses ni cuántos viajan: los toma del perfil y no vuelve a preguntar.
        var response = await (await tourist.PostAsJsonAsync($"/api/ai/conversations/{conversation!.Id}/messages",
            new { content = $"Quiero 2 días en {cityName}" })).Content.ReadFromJsonAsync<SendMessageResponse>(JsonOptions);

        Assert.False(response!.ClarificationNeeded);
        Assert.Equal(1, response.ParsedPreferences.TravelersCount);
        Assert.Contains(response.ProfileHints, h => h.Contains(category.Name));
        Assert.NotNull(response.Itinerary);
        Assert.Contains(response.Itinerary!.Items, i => i.ExperienceId == experience.Id);
    }
}
