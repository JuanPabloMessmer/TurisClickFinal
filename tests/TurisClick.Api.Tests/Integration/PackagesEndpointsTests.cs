using System.Net;
using System.Net.Http.Json;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Modules.Packages.Dtos;
using TurisClick.Api.Shared.Responses;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>UC-P-07/08/09, UC-T-06/07 — CRUD, itinerario por días, publicación y catálogo público de Package.</summary>
[Collection(ApiCollection.Name)]
public class PackagesEndpointsTests
{
    private readonly TurisClickApiFactory _factory;

    public PackagesEndpointsTests(TurisClickApiFactory factory) => _factory = factory;

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

    /// <summary>Registra un Provider aprobado y le crea una Experience propia (DRAFT) para usar como PackageItem EXPERIENCE_REFERENCE.</summary>
    private async Task<(HttpClient ProviderClient, Guid DestinationId, ExperienceResponse Experience)> CreateOwnerWithExperienceAsync(string emailPrefix)
    {
        var adminClient = _factory.CreateClient();
        var destinationId = await CreateCityDestinationAsync(adminClient);

        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), emailPrefix);
        UseBearerToken(providerClient, provider.AccessToken);

        var experience = await (await providerClient.PostAsJsonAsync("/api/experiences", new
        {
            title = $"Cementerio de Trenes-{Guid.NewGuid():N}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId,
            categoryIds = Array.Empty<Guid>(),
            price = 20m,
            currency = "USD"
        })).Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);

        return (providerClient, destinationId, experience!);
    }

    private static object ValidPayload(Guid destinationId, Guid experienceId, string title = "Uyuni Experience — 3 días") => new
    {
        title,
        description = "Recorrido de tres días por el Salar de Uyuni y alrededores.",
        destinationId,
        categoryIds = Array.Empty<Guid>(),
        conditionsText = "Incluye transporte y guía. No incluye vuelos.",
        durationDays = 3,
        price = 450m,
        currency = "USD",
        items = new object[]
        {
            new { dayNumber = 1, sortOrder = 0, kind = "EXPERIENCE_REFERENCE", experienceId },
            new { dayNumber = 1, sortOrder = 1, kind = "DESCRIPTIVE", title = "Colchani", description = "Parada en el pueblo salinero." },
            new { dayNumber = 2, sortOrder = 0, kind = "DESCRIPTIVE", title = "Lagunas Altiplánicas" },
            new { dayNumber = 3, sortOrder = 0, kind = "DESCRIPTIVE", title = "Retorno" }
        },
        images = new object[]
        {
            new { url = "https://picsum.photos/seed/uyuni1/800/600", isCover = true },
            new { url = "https://picsum.photos/seed/uyuni2/800/600", isCover = false }
        }
    };

    [Fact]
    public async Task Create_AsApprovedProvider_Returns201InDraftWithItemsAndCoverImage()
    {
        var (providerClient, destinationId, experience) = await CreateOwnerWithExperienceAsync("pkg-create");

        var response = await providerClient.PostAsJsonAsync("/api/packages", ValidPayload(destinationId, experience.Id));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PackageResponse>(JsonOptions);
        Assert.Equal("DRAFT", body!.Status);
        Assert.Equal(4, body.Items.Count);
        Assert.Equal(2, body.Images.Count);
        Assert.Single(body.Images, i => i.IsCover);
        // El ítem EXPERIENCE_REFERENCE sin Title propio muestra el título real de la Experience.
        var experienceItem = body.Items.Single(i => i.Kind == "EXPERIENCE_REFERENCE");
        Assert.Equal(experience.Title, experienceItem.Title);
    }

    [Fact]
    public async Task Create_AsUnapprovedProvider_Returns403()
    {
        var adminClient = _factory.CreateClient();
        var destinationId = await CreateCityDestinationAsync(adminClient);

        var providerClient = _factory.CreateClient();
        var provider = await RegisterProviderAsync(providerClient, "pkg-unapproved"); // sin aprobar
        UseBearerToken(providerClient, provider.AccessToken);

        var response = await providerClient.PostAsJsonAsync("/api/packages", new
        {
            title = "Paquete sin aprobar",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId,
            categoryIds = Array.Empty<Guid>(),
            durationDays = 1,
            price = 100m,
            currency = "USD",
            items = Array.Empty<object>(),
            images = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_AsTourist_Returns403()
    {
        var adminClient = _factory.CreateClient();
        var destinationId = await CreateCityDestinationAsync(adminClient);

        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "pkg-tourist"));

        var response = await touristClient.PostAsJsonAsync("/api/packages", new
        {
            title = "Paquete de turista",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId,
            categoryIds = Array.Empty<Guid>(),
            durationDays = 1,
            price = 100m,
            currency = "USD",
            items = Array.Empty<object>(),
            images = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_ExperienceFromAnotherCompany_Returns400()
    {
        var (_, destinationId, attackerExperience) = await CreateOwnerWithExperienceAsync("pkg-xcompany-attacker");

        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), "pkg-xcompany-owner");
        UseBearerToken(providerClient, provider.AccessToken);

        var response = await providerClient.PostAsJsonAsync("/api/packages", ValidPayload(destinationId, attackerExperience.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DestinationIsNotCity_Returns400()
    {
        var adminClient = _factory.CreateClient();
        UseBearerToken(adminClient, await LoginAsAdminAsync(adminClient));
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var country = await (await adminClient.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"País-{suffix}", type = "COUNTRY" })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);

        var (providerClient, _, experience) = await CreateOwnerWithExperienceAsync("pkg-badcity");

        var response = await providerClient.PostAsJsonAsync("/api/packages", ValidPayload(country!.Id, experience.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_ItemDayNumberExceedsDurationDays_Returns400()
    {
        var (providerClient, destinationId, experience) = await CreateOwnerWithExperienceAsync("pkg-daytoolarge");

        var response = await providerClient.PostAsJsonAsync("/api/packages", new
        {
            title = "Paquete con día inválido",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId,
            categoryIds = Array.Empty<Guid>(),
            durationDays = 2,
            price = 100m,
            currency = "USD",
            items = new object[] { new { dayNumber = 5, sortOrder = 0, kind = "EXPERIENCE_REFERENCE", experienceId = experience.Id } },
            images = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DescriptiveItemWithoutTitle_Returns400()
    {
        var (providerClient, destinationId, _) = await CreateOwnerWithExperienceAsync("pkg-descnoTitle");

        var response = await providerClient.PostAsJsonAsync("/api/packages", new
        {
            title = "Paquete con ítem descriptivo sin título",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId,
            categoryIds = Array.Empty<Guid>(),
            durationDays = 1,
            price = 100m,
            currency = "USD",
            items = new object[] { new { dayNumber = 1, sortOrder = 0, kind = "DESCRIPTIVE" } },
            images = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_MoreThanOneCoverImage_Returns400()
    {
        var (providerClient, destinationId, experience) = await CreateOwnerWithExperienceAsync("pkg-2covers");

        var response = await providerClient.PostAsJsonAsync("/api/packages", new
        {
            title = "Paquete con dos portadas",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId,
            categoryIds = Array.Empty<Guid>(),
            durationDays = 1,
            price = 100m,
            currency = "USD",
            items = new object[] { new { dayNumber = 1, sortOrder = 0, kind = "EXPERIENCE_REFERENCE", experienceId = experience.Id } },
            images = new object[]
            {
                new { url = "https://picsum.photos/seed/a/800/600", isCover = true },
                new { url = "https://picsum.photos/seed/b/800/600", isCover = true }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_PackageOfAnotherCompany_Returns403()
    {
        var (ownerClient, destinationId, experience) = await CreateOwnerWithExperienceAsync("pkg-owner");
        var created = await (await ownerClient.PostAsJsonAsync("/api/packages", ValidPayload(destinationId, experience.Id)))
            .Content.ReadFromJsonAsync<PackageResponse>(JsonOptions);

        var attackerClient = _factory.CreateClient();
        var attacker = await RegisterApprovedProviderAsync(attackerClient, _factory.CreateClient(), "pkg-attacker");
        UseBearerToken(attackerClient, attacker.AccessToken);

        var response = await attackerClient.PutAsJsonAsync($"/api/packages/{created!.Id}",
            ValidPayload(destinationId, experience.Id, "Título hackeado"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Confirma que el ataque no tuvo ningún efecto: el dueño real sigue viendo el título original
        // (ownerClient ya conserva su propio bearer token desde CreateOwnerWithExperienceAsync).
        var stillOwned = await (await ownerClient.GetAsync($"/api/packages/mine/{created.Id}"))
            .Content.ReadFromJsonAsync<PackageResponse>(JsonOptions);
        Assert.Equal("Uyuni Experience — 3 días", stillOwned!.Title);
    }

    [Fact]
    public async Task Publish_WithoutItems_Returns409()
    {
        var (providerClient, destinationId, _) = await CreateOwnerWithExperienceAsync("pkg-publish-noitems");

        var created = await (await providerClient.PostAsJsonAsync("/api/packages", new
        {
            title = "Paquete sin ítems",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId,
            categoryIds = Array.Empty<Guid>(),
            durationDays = 1,
            price = 100m,
            currency = "USD",
            items = Array.Empty<object>(),
            images = Array.Empty<object>()
        })).Content.ReadFromJsonAsync<PackageResponse>(JsonOptions);

        var response = await providerClient.PostAsync($"/api/packages/{created!.Id}/publish", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Publish_WithItemsButWithoutAvailability_Returns409()
    {
        var (providerClient, destinationId, experience) = await CreateOwnerWithExperienceAsync("pkg-publish-noavail");
        var created = await (await providerClient.PostAsJsonAsync("/api/packages", ValidPayload(destinationId, experience.Id)))
            .Content.ReadFromJsonAsync<PackageResponse>(JsonOptions);

        var response = await providerClient.PostAsync($"/api/packages/{created!.Id}/publish", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Publish_WithItemsAndFutureAvailability_MakesItVisibleInPublicSearchAndDetail()
    {
        var (providerClient, destinationId, experience) = await CreateOwnerWithExperienceAsync("pkg-publish-ok");
        var title = $"Publicado-{Guid.NewGuid():N}";
        var created = await (await providerClient.PostAsJsonAsync("/api/packages", ValidPayload(destinationId, experience.Id, title)))
            .Content.ReadFromJsonAsync<PackageResponse>(JsonOptions);

        await providerClient.PostAsJsonAsync($"/api/packages/{created!.Id}/availability", new
        {
            departureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15)),
            totalSlots = 12
        });

        var publishResponse = await providerClient.PostAsync($"/api/packages/{created.Id}/publish", null);
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);

        var anonymousClient = _factory.CreateClient();
        var publicDetail = await anonymousClient.GetAsync($"/api/packages/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, publicDetail.StatusCode);

        var searchResponse = await anonymousClient.GetAsync($"/api/packages?destinationId={destinationId}");
        var searchBody = await searchResponse.Content.ReadFromJsonAsync<PagedResult<PackageSummaryResponse>>(JsonOptions);
        Assert.Contains(searchBody!.Items, p => p.Id == created.Id);
    }

    [Fact]
    public async Task GetPublishedById_PackageStillDraft_Returns404ToAnonymousUser()
    {
        var (providerClient, destinationId, experience) = await CreateOwnerWithExperienceAsync("pkg-draft-hidden");
        var created = await (await providerClient.PostAsJsonAsync("/api/packages", ValidPayload(destinationId, experience.Id)))
            .Content.ReadFromJsonAsync<PackageResponse>(JsonOptions);

        var anonymousClient = _factory.CreateClient();
        var response = await anonymousClient.GetAsync($"/api/packages/{created!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Search_DoesNotReturnUnpublishedOrSuspendedPackages()
    {
        var (providerClient, destinationId, experience) = await CreateOwnerWithExperienceAsync("pkg-search-hidden");
        var title = $"NuncaPublicado-{Guid.NewGuid():N}";
        var created = await (await providerClient.PostAsJsonAsync("/api/packages", ValidPayload(destinationId, experience.Id, title)))
            .Content.ReadFromJsonAsync<PackageResponse>(JsonOptions);

        var anonymousClient = _factory.CreateClient();
        var searchResponse = await anonymousClient.GetAsync($"/api/packages?destinationId={destinationId}");
        var searchBody = await searchResponse.Content.ReadFromJsonAsync<PagedResult<PackageSummaryResponse>>(JsonOptions);

        Assert.DoesNotContain(searchBody!.Items, p => p.Id == created!.Id);
    }

    [Fact]
    public async Task ListMine_ReturnsOnlyOwnCompanyPackages()
    {
        var (providerAClient, destinationId, experienceA) = await CreateOwnerWithExperienceAsync("pkg-mine-a");
        var createdA = await (await providerAClient.PostAsJsonAsync("/api/packages", ValidPayload(destinationId, experienceA.Id, $"DeA-{Guid.NewGuid():N}")))
            .Content.ReadFromJsonAsync<PackageResponse>(JsonOptions);

        var (providerBClient, destinationB, experienceB) = await CreateOwnerWithExperienceAsync("pkg-mine-b");
        await providerBClient.PostAsJsonAsync("/api/packages", ValidPayload(destinationB, experienceB.Id, $"DeB-{Guid.NewGuid():N}"));

        var mineResponse = await providerAClient.GetAsync("/api/packages/mine");
        var mineBody = await mineResponse.Content.ReadFromJsonAsync<PagedResult<PackageSummaryResponse>>(JsonOptions);

        Assert.Contains(mineBody!.Items, item => item.Id == createdA!.Id);
        Assert.DoesNotContain(mineBody.Items, item => item.Title.StartsWith("DeB-"));
    }
}
