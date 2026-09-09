using System.Net;
using System.Net.Http.Json;
using TurisClick.Api.Modules.Ai.Dtos;
using TurisClick.Api.Modules.Categories.Dtos;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Shared.Responses;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// UC-T-15/16/17 y UC-AI-05/06 (Oleada 6) contra PostgreSQL real, con el proveedor "Deterministic"
/// (nunca Ollama). Cubre el ciclo completo: generar → iterar → guardar → listar → retomar, más las
/// revalidaciones al retomar (precio cambiado / sin disponibilidad) y el aislamiento entre turistas.
/// </summary>
[Collection(ApiCollection.Name)]
public class AiItineraryLifecycleTests
{
    private readonly TurisClickApiFactory _factory;

    public AiItineraryLifecycleTests(TurisClickApiFactory factory) => _factory = factory;

    /// <summary>Fecha relativa compartida por el seed y por el mensaje de prueba — nunca hardcodeada.</summary>
    private static DateOnly AvailabilityDate => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));

    private sealed record Catalog(
        Guid CityId,
        string CityName,
        string CategoryName,
        Guid ExperienceAId,
        Guid ExperienceBId,
        Guid AvailabilityAId,
        HttpClient ProviderClient);

    /// <summary>Dos experiencias publicadas en la misma ciudad: una para conservar, otra para reemplazar.</summary>
    private async Task<Catalog> SeedCatalogAsync(string emailPrefix)
    {
        var adminClient = _factory.CreateClient();
        UseBearerToken(adminClient, await LoginAsAdminAsync(adminClient));
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var country = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = $"PaisIter{suffix}", type = "COUNTRY" }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var region = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = $"RegionIter{suffix}", type = "REGION", parentId = country!.Id }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var cityName = $"CiudadIter{suffix}";
        var city = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = cityName, type = "CITY", parentId = region!.Id }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);

        var categoryName = $"InteresIter{suffix}";
        var category = await (await adminClient.PostAsJsonAsync("/api/admin/categories", new { name = categoryName }))
            .Content.ReadFromJsonAsync<CategoryResponse>(JsonOptions);

        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), emailPrefix);
        UseBearerToken(providerClient, provider.AccessToken);

        async Task<(Guid Id, Guid AvailabilityId)> CreatePublishedExperienceAsync(string title, decimal price)
        {
            var experience = await (await providerClient.PostAsJsonAsync("/api/experiences", new
            {
                title,
                description = "Descripción suficientemente larga para pasar la validación.",
                destinationId = city!.Id,
                categoryIds = new[] { category!.Id },
                price,
                currency = "USD"
            })).Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);

            var availability = await (await providerClient.PostAsJsonAsync($"/api/experiences/{experience!.Id}/availability", new
            {
                date = AvailabilityDate,
                totalSlots = 10
            })).Content.ReadFromJsonAsync<ExperienceAvailabilityResponse>(JsonOptions);

            await providerClient.PostAsync($"/api/experiences/{experience.Id}/publish", null);
            return (experience.Id, availability!.Id);
        }

        var a = await CreatePublishedExperienceAsync($"Tour Iter A {suffix}", 40);
        var b = await CreatePublishedExperienceAsync($"Tour Iter B {suffix}", 60);

        return new Catalog(city!.Id, cityName, categoryName, a.Id, b.Id, a.AvailabilityId, providerClient);
    }

    /// <summary>Conversación con un itinerario ya generado (UC-T-12/13/14), lista para iterar.</summary>
    private async Task<(HttpClient Tourist, ConversationResponse Conversation, ItineraryResponse Itinerary)>
        StartConversationWithItineraryAsync(Catalog catalog, string touristPrefix)
    {
        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, touristPrefix));

        var conversation = await (await touristClient.PostAsync("/api/ai/conversations", null))
            .Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions);

        var from = AvailabilityDate.AddDays(-2);
        var to = AvailabilityDate.AddDays(2);
        var generated = await (await touristClient.PostAsJsonAsync($"/api/ai/conversations/{conversation!.Id}/messages",
            new { content = $"Quiero ir a {catalog.CityName} del {from:yyyy-MM-dd} al {to:yyyy-MM-dd}, somos 2 personas." }))
            .Content.ReadFromJsonAsync<SendMessageResponse>(JsonOptions);

        Assert.NotNull(generated!.Itinerary);
        return (touristClient, conversation, generated.Itinerary!);
    }

    [Fact]
    public async Task Iterate_RemovesRequestedItem_KeepsTheRestAndCreatesANewVersion()
    {
        var catalog = await SeedCatalogAsync("ai-iter-remove");
        var (tourist, conversation, itinerary) = await StartConversationWithItineraryAsync(catalog, "ai-iter-remove-t");
        Assert.Equal(2, itinerary.Items.Count); // ambas experiencias entraron en la propuesta inicial

        // El título real del producto es lo que hace match en el proveedor determinístico.
        var target = itinerary.Items.First();
        var targetTitle = target.ExperienceTitle!;

        var response = await tourist.PostAsJsonAsync($"/api/ai/conversations/{conversation.Id}/messages",
            new { content = $"Quitá {targetTitle}." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SendMessageResponse>(JsonOptions);

        Assert.NotNull(body!.Itinerary);
        Assert.Equal(2, body.Itinerary!.Version);                                 // nueva versión
        Assert.NotEqual(itinerary.Id, body.Itinerary.Id);                         // la anterior se conserva
        Assert.Single(body.Itinerary.Items);
        Assert.DoesNotContain(body.Itinerary.Items, i => i.ExperienceId == target.ExperienceId);

        // UC-T-14 devuelve la versión vigente, no la vieja.
        var latest = await (await tourist.GetAsync($"/api/ai/conversations/{conversation.Id}/itinerary"))
            .Content.ReadFromJsonAsync<ItineraryResponse>(JsonOptions);
        Assert.Equal(body.Itinerary.Id, latest!.Id);

        // Y la versión anterior sigue existiendo, con sus dos ítems (trazabilidad de la iteración).
        var previous = await (await tourist.GetAsync($"/api/ai/itineraries/{itinerary.Id}"))
            .Content.ReadFromJsonAsync<ItineraryResponse>(JsonOptions);
        Assert.Equal(2, previous!.Items.Count);
    }

    [Fact]
    public async Task Iterate_ReplacesOneDay_KeepingTheOtherItemUntouched()
    {
        var catalog = await SeedCatalogAsync("ai-iter-replace");
        var (tourist, conversation, itinerary) = await StartConversationWithItineraryAsync(catalog, "ai-iter-replace-t");

        var dayOneItem = itinerary.Items.Single(i => i.DayNumber == 1);
        var dayTwoItem = itinerary.Items.Single(i => i.DayNumber == 2);

        var body = await (await tourist.PostAsJsonAsync($"/api/ai/conversations/{conversation.Id}/messages",
            new { content = "Cambiá el día 2." })).Content.ReadFromJsonAsync<SendMessageResponse>(JsonOptions);

        Assert.NotNull(body!.Itinerary);
        // El día 1 se conserva tal cual; el día 2 ya no puede ser el mismo producto que antes.
        Assert.Contains(body.Itinerary!.Items, i => i.ExperienceId == dayOneItem.ExperienceId);
        Assert.DoesNotContain(body.Itinerary.Items, i => i.ExperienceId == dayTwoItem.ExperienceId);
    }

    [Fact]
    public async Task SaveAndResume_ListsItineraryAndAllowsContinuingTheConversation()
    {
        var catalog = await SeedCatalogAsync("ai-save");
        var (tourist, conversation, itinerary) = await StartConversationWithItineraryAsync(catalog, "ai-save-t");

        // UC-T-16 — guardar.
        var saved = await tourist.PostAsync($"/api/ai/itineraries/{itinerary.Id}/save", null);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("SAVED", (await saved.Content.ReadFromJsonAsync<ItineraryResponse>(JsonOptions))!.Status);

        // Idempotente: guardar de nuevo no falla ni duplica.
        var savedAgain = await tourist.PostAsync($"/api/ai/itineraries/{itinerary.Id}/save", null);
        Assert.Equal(HttpStatusCode.OK, savedAgain.StatusCode);

        // UC-T-17 — aparece en "Mis itinerarios guardados".
        var list = await (await tourist.GetAsync("/api/ai/itineraries/me"))
            .Content.ReadFromJsonAsync<PagedResult<SavedItinerarySummaryResponse>>(JsonOptions);
        var row = Assert.Single(list!.Items, i => i.Id == itinerary.Id);
        Assert.Equal(conversation.Id, row.AiConversationId); // permite volver a la conversación
        Assert.NotEmpty(row.Totals);

        // Retomar: se recupera el itinerario y se puede seguir iterando sobre la misma conversación.
        var resumed = await (await tourist.GetAsync($"/api/ai/itineraries/{itinerary.Id}"))
            .Content.ReadFromJsonAsync<ItineraryResponse>(JsonOptions);
        Assert.Equal("SAVED", resumed!.Status);
        Assert.True(resumed.IsStillBookable);
        Assert.Empty(resumed.Warnings);

        var continued = await tourist.PostAsJsonAsync($"/api/ai/conversations/{resumed.AiConversationId}/messages",
            new { content = "Quiero gastar menos." });
        Assert.Equal(HttpStatusCode.OK, continued.StatusCode);
    }

    [Fact]
    public async Task Resume_AfterProviderRaisesPrice_ShowsSnapshotAndCurrentPriceWithWarning()
    {
        var catalog = await SeedCatalogAsync("ai-price");
        var (tourist, _, itinerary) = await StartConversationWithItineraryAsync(catalog, "ai-price-t");
        await tourist.PostAsync($"/api/ai/itineraries/{itinerary.Id}/save", null);

        var item = itinerary.Items.First(i => i.ExperienceId == catalog.ExperienceAId);
        var originalPrice = item.EstimatedUnitPrice;

        // El proveedor sube el precio DESPUÉS de que el turista guardó su itinerario.
        var updated = await catalog.ProviderClient.PutAsJsonAsync($"/api/experiences/{catalog.ExperienceAId}", new
        {
            title = $"Tour Iter A actualizado {Guid.NewGuid().ToString("N")[..6]}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = catalog.CityId,
            categoryIds = Array.Empty<Guid>(),
            price = originalPrice + 15,
            currency = "USD"
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var resumed = await (await tourist.GetAsync($"/api/ai/itineraries/{itinerary.Id}"))
            .Content.ReadFromJsonAsync<ItineraryResponse>(JsonOptions);

        var resumedItem = Assert.Single(resumed!.Items, i => i.ExperienceId == catalog.ExperienceAId);
        Assert.Equal(originalPrice, resumedItem.EstimatedUnitPrice);            // el snapshot NO se toca
        Assert.Equal(originalPrice + 15, resumedItem.CurrentPrice);             // el precio vigente va al lado
        Assert.Contains(resumed.Warnings, w => w.Contains("precio", StringComparison.OrdinalIgnoreCase));
        Assert.True(resumed.IsStillBookable);                                   // subió de precio, pero se puede reservar
    }

    [Fact]
    public async Task Iterate_AfterPriceChange_PreservedItemKeepsItsSnapshotAndStillReportsTheCurrentPrice()
    {
        var catalog = await SeedCatalogAsync("ai-iter-price");
        var (tourist, conversation, itinerary) = await StartConversationWithItineraryAsync(catalog, "ai-iter-price-t");

        var preserved = itinerary.Items.Single(i => i.ExperienceId == catalog.ExperienceAId);
        var toRemove = itinerary.Items.Single(i => i.ExperienceId == catalog.ExperienceBId);
        var originalPrice = preserved.EstimatedUnitPrice;

        // El proveedor sube el precio del componente que el turista NO va a tocar.
        await catalog.ProviderClient.PutAsJsonAsync($"/api/experiences/{catalog.ExperienceAId}", new
        {
            title = $"Tour Iter A caro {Guid.NewGuid().ToString("N")[..6]}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = catalog.CityId,
            categoryIds = Array.Empty<Guid>(),
            price = originalPrice + 25,
            currency = "USD"
        });

        // Ahora itera pidiendo quitar el OTRO componente.
        var body = await (await tourist.PostAsJsonAsync($"/api/ai/conversations/{conversation.Id}/messages",
            new { content = $"Quitá {toRemove.ExperienceTitle}." })).Content.ReadFromJsonAsync<SendMessageResponse>(JsonOptions);

        Assert.NotNull(body!.Itinerary);
        var carried = Assert.Single(body.Itinerary!.Items);
        Assert.Equal(catalog.ExperienceAId, carried.ExperienceId);
        // Preservar es preservar: el ítem viaja a la versión nueva con SU snapshot original.
        Assert.Equal(originalPrice, carried.EstimatedUnitPrice);
        Assert.Contains(body.Warnings, w => w.Contains("precio", StringComparison.OrdinalIgnoreCase));

        // Y al leer la versión nueva, snapshot y precio vigente coexisten: el cambio no se pierde.
        var reread = await (await tourist.GetAsync($"/api/ai/itineraries/{body.Itinerary.Id}"))
            .Content.ReadFromJsonAsync<ItineraryResponse>(JsonOptions);
        var rereadItem = Assert.Single(reread!.Items);
        Assert.Equal(originalPrice, rereadItem.EstimatedUnitPrice);
        Assert.Equal(originalPrice + 25, rereadItem.CurrentPrice);
        Assert.True(rereadItem.PriceChanged);
        Assert.Equal("AVAILABLE", rereadItem.AvailabilityState);
    }

    [Fact]
    public async Task Resume_IsReadOnly_SnapshotSurvivesRepeatedReads()
    {
        var catalog = await SeedCatalogAsync("ai-readonly");
        var (tourist, _, itinerary) = await StartConversationWithItineraryAsync(catalog, "ai-readonly-t");
        await tourist.PostAsync($"/api/ai/itineraries/{itinerary.Id}/save", null);

        var item = itinerary.Items.First(i => i.ExperienceId == catalog.ExperienceAId);

        await catalog.ProviderClient.PutAsJsonAsync($"/api/experiences/{catalog.ExperienceAId}", new
        {
            title = $"Tour Iter A v2 {Guid.NewGuid().ToString("N")[..6]}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = catalog.CityId,
            categoryIds = Array.Empty<Guid>(),
            price = item.EstimatedUnitPrice + 30,
            currency = "USD"
        });

        // Tres lecturas seguidas: revalidar no puede ir corriendo el snapshot hacia el precio vigente.
        for (var i = 0; i < 3; i++)
        {
            var read = await (await tourist.GetAsync($"/api/ai/itineraries/{itinerary.Id}"))
                .Content.ReadFromJsonAsync<ItineraryResponse>(JsonOptions);
            var readItem = Assert.Single(read!.Items, x => x.ExperienceId == catalog.ExperienceAId);
            Assert.Equal(item.EstimatedUnitPrice, readItem.EstimatedUnitPrice);
            Assert.Equal(item.EstimatedUnitPrice + 30, readItem.CurrentPrice);
        }
    }

    [Fact]
    public async Task Resume_AfterProviderUnpublishes_MarksItineraryAsNotBookable()
    {
        var catalog = await SeedCatalogAsync("ai-unpub");
        var (tourist, _, itinerary) = await StartConversationWithItineraryAsync(catalog, "ai-unpub-t");
        await tourist.PostAsync($"/api/ai/itineraries/{itinerary.Id}/save", null);

        var unpublished = await catalog.ProviderClient.PostAsync($"/api/experiences/{catalog.ExperienceAId}/unpublish", null);
        Assert.Equal(HttpStatusCode.OK, unpublished.StatusCode);

        var resumed = await (await tourist.GetAsync($"/api/ai/itineraries/{itinerary.Id}"))
            .Content.ReadFromJsonAsync<ItineraryResponse>(JsonOptions);

        Assert.False(resumed!.IsStillBookable);
        var affected = Assert.Single(resumed.Items, i => i.ExperienceId == catalog.ExperienceAId);
        Assert.False(affected.IsStillAvailable);
        Assert.Equal("UNPUBLISHED", affected.AvailabilityState); // causa estructurada, no solo prosa
        Assert.Contains(resumed.Warnings, w => w.Contains("publicada", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ItemExplanation_UsesRealCatalogFacts()
    {
        var catalog = await SeedCatalogAsync("ai-explain");
        var (tourist, _, itinerary) = await StartConversationWithItineraryAsync(catalog, "ai-explain-t");
        var item = itinerary.Items.First();

        var response = await tourist.GetAsync($"/api/ai/itineraries/{itinerary.Id}/items/{item.Id}/explanation");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ItemExplanationResponse>(JsonOptions);

        Assert.Equal(item.Id, body!.ItemId);
        Assert.NotEmpty(body.Facts);
        // Los hechos salen de la base: destino real, cupos reales y el precio real de la propuesta.
        Assert.Contains(body.Facts, f => f.Contains(catalog.CityName));
        Assert.Contains(body.Facts, f => f.Contains("cupo"));
        Assert.Contains(body.Facts, f => f.Contains($"USD {item.EstimatedUnitPrice:0.##}"));
        Assert.False(string.IsNullOrWhiteSpace(body.Explanation));
    }

    [Fact]
    public async Task Ownership_AnotherTouristCannotReadSaveOrExplainSomeoneElsesItinerary()
    {
        var catalog = await SeedCatalogAsync("ai-own");
        var (_, conversation, itinerary) = await StartConversationWithItineraryAsync(catalog, "ai-own-a");

        var intruder = _factory.CreateClient();
        UseBearerToken(intruder, await RegisterAndLoginTouristAsync(intruder, "ai-own-b"));

        Assert.Equal(HttpStatusCode.Forbidden, (await intruder.GetAsync($"/api/ai/itineraries/{itinerary.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await intruder.PostAsync($"/api/ai/itineraries/{itinerary.Id}/save", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await intruder.GetAsync($"/api/ai/itineraries/{itinerary.Id}/items/{itinerary.Items.First().Id}/explanation")).StatusCode);
        // Tampoco puede seguir iterando la conversación ajena.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await intruder.PostAsJsonAsync($"/api/ai/conversations/{conversation.Id}/messages", new { content = "Cambiá el día 1." })).StatusCode);

        // Y el itinerario ajeno no aparece en su propia lista de guardados.
        var list = await (await intruder.GetAsync("/api/ai/itineraries/me"))
            .Content.ReadFromJsonAsync<PagedResult<SavedItinerarySummaryResponse>>(JsonOptions);
        Assert.DoesNotContain(list!.Items, i => i.Id == itinerary.Id);
    }
}
