using System.Net;
using System.Net.Http.Json;
using TurisClick.Api.Modules.Admin.Dtos;
using TurisClick.Api.Modules.Auth.Dtos;
using TurisClick.Api.Modules.Ai.Dtos;
using TurisClick.Api.Modules.Categories.Dtos;
using TurisClick.Api.Modules.Companies.Dtos;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Modules.Packages.Dtos;
using TurisClick.Api.Shared.Responses;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// UC-A-06/07/08 (Oleada 8). El foco no es que el enum cambie en la base, sino que la sanción se
/// aplique de verdad: que el usuario no pueda entrar, que el proveedor no pueda deshacer la suspensión
/// de su contenido, y que el catálogo de una empresa suspendida desaparezca del público y de la IA.
/// </summary>
[Collection(ApiCollection.Name)]
public class AdminSanctionsTests
{
    private readonly TurisClickApiFactory _factory;

    public AdminSanctionsTests(TurisClickApiFactory factory) => _factory = factory;

    private static DateOnly AvailabilityDate => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await LoginAsAdminAsync(client));
        return client;
    }

    private sealed record Catalog(
        Guid CityId, string CityName, Guid CompanyId,
        Guid ExperienceId, Guid PackageId,
        HttpClient Provider);

    private async Task<Catalog> SeedCatalogAsync(string prefix)
    {
        var adminClient = await AdminClientAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var country = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = $"PaisAdm{suffix}", type = "COUNTRY" }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var region = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = $"RegionAdm{suffix}", type = "REGION", parentId = country!.Id }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var cityName = $"CiudadAdm{suffix}";
        var city = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = cityName, type = "CITY", parentId = region!.Id }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var category = await (await adminClient.PostAsJsonAsync("/api/admin/categories", new { name = $"InteresAdm{suffix}" }))
            .Content.ReadFromJsonAsync<CategoryResponse>(JsonOptions);

        var provider = _factory.CreateClient();
        var registration = await RegisterApprovedProviderAsync(provider, _factory.CreateClient(), prefix);
        UseBearerToken(provider, registration.AccessToken);

        var experience = await (await provider.PostAsJsonAsync("/api/experiences", new
        {
            title = $"Tour Adm {suffix}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = city!.Id,
            categoryIds = new[] { category!.Id },
            price = 40,
            currency = "USD"
        })).Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);

        await provider.PostAsJsonAsync($"/api/experiences/{experience!.Id}/availability",
            new { date = AvailabilityDate, totalSlots = 10 });
        await provider.PostAsync($"/api/experiences/{experience.Id}/publish", null);

        var package = await (await provider.PostAsJsonAsync("/api/packages", new
        {
            title = $"Paquete Adm {suffix}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = city.Id,
            categoryIds = new[] { category.Id },
            durationDays = 1,
            price = 300,
            currency = "USD",
            items = new object[] { new { dayNumber = 1, sortOrder = 0, kind = "EXPERIENCE_REFERENCE", experienceId = experience.Id } },
            images = Array.Empty<object>()
        })).Content.ReadFromJsonAsync<PackageResponse>(JsonOptions);

        await provider.PostAsJsonAsync($"/api/packages/{package!.Id}/availability",
            new { departureDate = AvailabilityDate, totalSlots = 10 });
        await provider.PostAsync($"/api/packages/{package.Id}/publish", null);

        return new Catalog(city.Id, cityName, registration.Company.Id, experience.Id, package.Id, provider);
    }

    /// <summary>Pide una propuesta a la IA y devuelve los ids de producto que el retrieval consideró candidatos.</summary>
    private async Task<List<Guid>> RetrievedProductIdsAsync(Catalog catalog, string touristPrefix)
    {
        var tourist = _factory.CreateClient();
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, touristPrefix));

        var conversation = await (await tourist.PostAsync("/api/ai/conversations", null))
            .Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions);

        var from = AvailabilityDate;
        var to = AvailabilityDate.AddDays(1);
        var generated = await (await tourist.PostAsJsonAsync($"/api/ai/conversations/{conversation!.Id}/messages",
            new { content = $"Quiero ir a {catalog.CityName} del {from:yyyy-MM-dd} al {to:yyyy-MM-dd}, somos 2 personas." }))
            .Content.ReadFromJsonAsync<SendMessageResponse>(JsonOptions);

        if (generated!.Itinerary is null) return [];

        return generated.Itinerary.Items
            .Select(i => i.ExperienceId ?? i.PackageId ?? Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToList();
    }

    // ---- UC-A-06: cuentas ----

    [Fact]
    public async Task AdminCanListAndFilterUsers()
    {
        var adminClient = await AdminClientAsync();
        var tourist = _factory.CreateClient();
        await RegisterAndLoginTouristAsync(tourist, "adm-list");

        var response = await adminClient.GetAsync("/api/admin/users?role=TOURIST&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<AdminUserResponse>>(JsonOptions);
        Assert.NotEmpty(body!.Items);
        Assert.All(body.Items, u => Assert.Equal("TOURIST", u.Role));
    }

    [Fact]
    public async Task SuspendedUser_CannotLoginOrRefresh()
    {
        var adminClient = await AdminClientAsync();

        var tourist = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"adm-susp-{suffix}@turisclick.dev";
        var registration = await tourist.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Turista", lastName = "Suspendido", email, password = "Password123!"
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var auth = await registration.Content.ReadFromJsonAsync<AuthResultResponse>(JsonOptions);

        var found = await (await adminClient.GetAsync($"/api/admin/users?search={email}"))
            .Content.ReadFromJsonAsync<PagedResult<AdminUserResponse>>(JsonOptions);
        var userId = Assert.Single(found!.Items).Id;

        var suspended = await adminClient.PostAsync($"/api/admin/users/{userId}/suspend", null);
        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);
        Assert.Equal("SUSPENDED", (await suspended.Content.ReadFromJsonAsync<AdminUserResponse>(JsonOptions))!.Status);

        // La sanción se aplica de verdad: ni login nuevo ni refresh del token que ya tenía.
        var login = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);

        var refresh = await _factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = auth!.RefreshToken });
        Assert.Equal(HttpStatusCode.Forbidden, refresh.StatusCode);

        // Y se puede revertir.
        Assert.Equal(HttpStatusCode.OK, (await adminClient.PostAsync($"/api/admin/users/{userId}/activate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" })).StatusCode);
    }

    [Fact]
    public async Task AdminCannotSuspendItsOwnAccount()
    {
        var adminClient = await AdminClientAsync();
        var admins = await (await adminClient.GetAsync("/api/admin/users?role=ADMIN"))
            .Content.ReadFromJsonAsync<PagedResult<AdminUserResponse>>(JsonOptions);
        var adminId = admins!.Items.Single(u => u.Email == TurisClickApiFactory.AdminEmail).Id;

        var response = await adminClient.PostAsync($"/api/admin/users/{adminId}/suspend", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task NonAdminCannotUseAdminUserEndpoints()
    {
        var tourist = _factory.CreateClient();
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, "adm-role"));

        Assert.Equal(HttpStatusCode.Forbidden, (await tourist.GetAsync("/api/admin/users")).StatusCode);
    }

    // ---- UC-A-07: contenido ----

    [Fact]
    public async Task SuspendedContent_DisappearsFromCatalogAndCannotBeRepublishedByItsProvider()
    {
        var catalog = await SeedCatalogAsync("adm-content");
        var adminClient = await AdminClientAsync();
        var publicClient = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await publicClient.GetAsync($"/api/experiences/{catalog.ExperienceId}")).StatusCode);

        var suspended = await adminClient.PostAsync($"/api/admin/experiences/{catalog.ExperienceId}/suspend", null);
        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);
        Assert.Equal("SUSPENDED", (await suspended.Content.ReadFromJsonAsync<AdminContentResponse>(JsonOptions))!.Status);

        // Para el turista deja de existir.
        Assert.Equal(HttpStatusCode.NotFound, (await publicClient.GetAsync($"/api/experiences/{catalog.ExperienceId}")).StatusCode);

        // Y el proveedor NO puede deshacer la sanción republicando.
        var republish = await catalog.Provider.PostAsync($"/api/experiences/{catalog.ExperienceId}/publish", null);
        Assert.Equal(HttpStatusCode.Conflict, republish.StatusCode);
        Assert.Contains("CONTENT_SUSPENDED", await republish.Content.ReadAsStringAsync());

        // Solo el admin la levanta, y queda en UNPUBLISHED (republicar sigue siendo decisión del proveedor).
        var restored = await adminClient.PostAsync($"/api/admin/experiences/{catalog.ExperienceId}/restore", null);
        Assert.Equal("UNPUBLISHED", (await restored.Content.ReadFromJsonAsync<AdminContentResponse>(JsonOptions))!.Status);
        Assert.Equal(HttpStatusCode.OK, (await catalog.Provider.PostAsync($"/api/experiences/{catalog.ExperienceId}/publish", null)).StatusCode);
    }

    [Fact]
    public async Task SuspendedPackage_CannotBeRepublishedByItsProvider()
    {
        var catalog = await SeedCatalogAsync("adm-pkg");
        var adminClient = await AdminClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await adminClient.PostAsync($"/api/admin/packages/{catalog.PackageId}/suspend", null)).StatusCode);

        var republish = await catalog.Provider.PostAsync($"/api/packages/{catalog.PackageId}/publish", null);
        Assert.Equal(HttpStatusCode.Conflict, republish.StatusCode);
        Assert.Contains("CONTENT_SUSPENDED", await republish.Content.ReadAsStringAsync());
    }

    // ---- UC-A-08: empresa ----

    [Fact]
    public async Task SuspendingACompany_HidesItsCatalogFromPublicAndFromRag_AndReactivationRestoresIt()
    {
        var catalog = await SeedCatalogAsync("adm-company");
        var adminClient = await AdminClientAsync();
        var publicClient = _factory.CreateClient();

        // Antes: visible para el público y candidato para la IA.
        Assert.Equal(HttpStatusCode.OK, (await publicClient.GetAsync($"/api/experiences/{catalog.ExperienceId}")).StatusCode);
        Assert.Contains(catalog.ExperienceId, await RetrievedProductIdsAsync(catalog, "adm-rag-1"));

        var suspended = await adminClient.PostAsync($"/api/admin/companies/{catalog.CompanyId}/suspend", null);
        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);
        Assert.Equal("SUSPENDED", (await suspended.Content.ReadFromJsonAsync<CompanyResponse>(JsonOptions))!.Status);

        // Después: desaparece del catálogo público...
        Assert.Equal(HttpStatusCode.NotFound, (await publicClient.GetAsync($"/api/experiences/{catalog.ExperienceId}")).StatusCode);
        var search = await (await publicClient.GetAsync($"/api/experiences?destinationId={catalog.CityId}"))
            .Content.ReadFromJsonAsync<PagedResult<ExperienceSummaryResponse>>(JsonOptions);
        Assert.DoesNotContain(search!.Items, e => e.Id == catalog.ExperienceId);

        // ...y del retrieval de la IA en la consulta siguiente (UC-SYS-09: no hay índice que reindexar).
        Assert.DoesNotContain(catalog.ExperienceId, await RetrievedProductIdsAsync(catalog, "adm-rag-2"));

        // El proveedor tampoco puede operar comercialmente mientras esté suspendido.
        await adminClient.PostAsync($"/api/admin/experiences/{catalog.ExperienceId}/suspend", null);
        await adminClient.PostAsync($"/api/admin/experiences/{catalog.ExperienceId}/restore", null);
        var publishWhileSuspended = await catalog.Provider.PostAsync($"/api/experiences/{catalog.ExperienceId}/publish", null);
        Assert.Equal(HttpStatusCode.Forbidden, publishWhileSuspended.StatusCode);
        Assert.Contains("COMPANY_SUSPENDED", await publishWhileSuspended.Content.ReadAsStringAsync());

        // Al reactivar, cada producto vuelve con el estado que conservó — nunca hubo cascada.
        Assert.Equal(HttpStatusCode.OK, (await adminClient.PostAsync($"/api/admin/companies/{catalog.CompanyId}/reactivate", null)).StatusCode);

        // El Package seguía PUBLISHED, así que reaparece solo.
        Assert.Equal(HttpStatusCode.OK, (await publicClient.GetAsync($"/api/packages/{catalog.PackageId}")).StatusCode);
        // La Experience quedó UNPUBLISHED por el restore de más arriba: sigue oculta hasta que su
        // proveedor decida republicarla. Eso es justamente "no restaurar estados por cascada".
        Assert.Equal(HttpStatusCode.NotFound, (await publicClient.GetAsync($"/api/experiences/{catalog.ExperienceId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await catalog.Provider.PostAsync($"/api/experiences/{catalog.ExperienceId}/publish", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await publicClient.GetAsync($"/api/experiences/{catalog.ExperienceId}")).StatusCode);
    }

    [Fact]
    public async Task TouristCannotReserveFromASuspendedCompany()
    {
        var catalog = await SeedCatalogAsync("adm-reserve");
        var adminClient = await AdminClientAsync();

        var availability = await (await catalog.Provider.GetAsync($"/api/experiences/{catalog.ExperienceId}/availability"))
            .Content.ReadFromJsonAsync<List<ExperienceAvailabilityResponse>>(JsonOptions);
        var availabilityId = availability!.First().Id;

        await adminClient.PostAsync($"/api/admin/companies/{catalog.CompanyId}/suspend", null);

        var tourist = _factory.CreateClient();
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, "adm-reserve-t"));

        // Ni siquiera conociendo el id de la disponibilidad: el producto no existe para él.
        var response = await tourist.PostAsJsonAsync("/api/reservations",
            new { experienceAvailabilityId = availabilityId, travelers = 2 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
