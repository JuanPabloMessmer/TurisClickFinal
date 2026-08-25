using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Reservations.Dtos;
using TurisClick.Api.Shared.Responses;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// UC-T-08/10, UC-P-12/13 — flujo completo de reserva directa: transacción, prevención de overbooking
/// (incluyendo un escenario de concurrencia real contra Postgres) y aislamiento TOURIST/PROVIDER.
/// </summary>
[Collection(ApiCollection.Name)]
public class ReservationsEndpointsTests
{
    private readonly TurisClickApiFactory _factory;

    public ReservationsEndpointsTests(TurisClickApiFactory factory) => _factory = factory;

    private async Task<(HttpClient ProviderClient, ExperienceResponse Experience, ExperienceAvailabilityResponse Availability)>
        CreatePublishedExperienceWithAvailabilityAsync(string emailPrefix, int totalSlots)
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
            price = 40m,
            currency = "USD"
        })).Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);

        var availability = await (await providerClient.PostAsJsonAsync($"/api/experiences/{experience!.Id}/availability", new
        {
            date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            totalSlots
        })).Content.ReadFromJsonAsync<ExperienceAvailabilityResponse>(JsonOptions);

        var publishResponse = await providerClient.PostAsync($"/api/experiences/{experience.Id}/publish", null);
        publishResponse.EnsureSuccessStatusCode();

        return (providerClient, experience, availability!);
    }

    [Fact]
    public async Task Create_ValidBooking_Returns201AndRetainsSlots()
    {
        var (_, _, availability) = await CreatePublishedExperienceWithAvailabilityAsync("res-create", totalSlots: 10);

        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "res-tourist"));

        var response = await touristClient.PostAsJsonAsync("/api/reservations", new
        {
            experienceAvailabilityId = availability.Id,
            travelers = 3
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        Assert.Equal("PENDING_PAYMENT", body!.Status);
        Assert.NotNull(body.ExpiresAt);
        Assert.Single(body.Items);
        Assert.Equal(3, body.Items[0].Travelers);
        Assert.Equal(120m, body.Items[0].Subtotal); // 40 * 3, precio congelado del servidor
        Assert.Single(body.Totals);
        Assert.Equal(120m, body.Totals[0].Amount);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TurisClickDbContext>();
        var updated = await db.ExperienceAvailabilities.AsNoTracking().SingleAsync(a => a.Id == availability.Id);
        Assert.Equal(3, updated.ReservedSlots);
    }

    [Fact]
    public async Task Create_NonExistentAvailability_Returns404()
    {
        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "res-missing"));

        var response = await touristClient.PostAsJsonAsync("/api/reservations", new
        {
            experienceAvailabilityId = Guid.NewGuid(),
            travelers = 1
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_UnpublishedExperience_Returns404()
    {
        var (providerClient, experience, availability) = await CreatePublishedExperienceWithAvailabilityAsync("res-unpub", totalSlots: 5);
        await providerClient.PostAsync($"/api/experiences/{experience.Id}/unpublish", null);

        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "res-unpub-t"));

        var response = await touristClient.PostAsJsonAsync("/api/reservations", new
        {
            experienceAvailabilityId = availability.Id,
            travelers = 1
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_NotEnoughCapacity_Returns409()
    {
        var (_, _, availability) = await CreatePublishedExperienceWithAvailabilityAsync("res-nocap", totalSlots: 2);

        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "res-nocap-t"));

        var response = await touristClient.PostAsJsonAsync("/api/reservations", new
        {
            experienceAvailabilityId = availability.Id,
            travelers = 3
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_ClosedSlot_Returns410()
    {
        var (_, _, availability) = await CreatePublishedExperienceWithAvailabilityAsync("res-closed", totalSlots: 5);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TurisClickDbContext>();
            var entity = await db.ExperienceAvailabilities.SingleAsync(a => a.Id == availability.Id);
            entity.Status = AvailabilitySlotStatus.CLOSED;
            await db.SaveChangesAsync();
        }

        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "res-closed-t"));

        var response = await touristClient.PostAsJsonAsync("/api/reservations", new
        {
            experienceAvailabilityId = availability.Id,
            travelers = 1
        });

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    /// <summary>
    /// UC-SYS-06 — el caso crítico de la oleada: dos reservas concurrentes compitiendo por el último cupo
    /// (TotalSlots = 1) contra Postgres real. Exactamente una debe ganar (201) y la otra debe perder (409);
    /// nunca ambas 201 (overbooking) ni ambas 409 (bug de bloqueo/deadlock).
    /// </summary>
    [Fact]
    public async Task Create_TwoConcurrentBookingsForLastSlot_OnlyOneSucceeds()
    {
        var (_, _, availability) = await CreatePublishedExperienceWithAvailabilityAsync("res-race", totalSlots: 1);

        var touristAClient = _factory.CreateClient();
        UseBearerToken(touristAClient, await RegisterAndLoginTouristAsync(touristAClient, "res-race-a"));
        var touristBClient = _factory.CreateClient();
        UseBearerToken(touristBClient, await RegisterAndLoginTouristAsync(touristBClient, "res-race-b"));

        var payload = new { experienceAvailabilityId = availability.Id, travelers = 1 };

        var taskA = touristAClient.PostAsJsonAsync("/api/reservations", payload);
        var taskB = touristBClient.PostAsJsonAsync("/api/reservations", payload);
        var results = await Task.WhenAll(taskA, taskB);

        var statusCodes = results.Select(r => r.StatusCode).ToList();
        Assert.Contains(HttpStatusCode.Created, statusCodes);
        Assert.Contains(HttpStatusCode.Conflict, statusCodes);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TurisClickDbContext>();
        var updated = await db.ExperienceAvailabilities.AsNoTracking().SingleAsync(a => a.Id == availability.Id);
        Assert.Equal(1, updated.ReservedSlots); // nunca 2 — sería overbooking
    }

    [Fact]
    public async Task GetById_AsOwner_Returns200()
    {
        var (_, _, availability) = await CreatePublishedExperienceWithAvailabilityAsync("res-get-owner", totalSlots: 5);

        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "res-get-owner-t"));

        var created = await (await touristClient.PostAsJsonAsync("/api/reservations",
            new { experienceAvailabilityId = availability.Id, travelers = 1 }))
            .Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        var response = await touristClient.GetAsync($"/api/reservations/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetById_AsAnotherTourist_Returns403()
    {
        var (_, _, availability) = await CreatePublishedExperienceWithAvailabilityAsync("res-get-other", totalSlots: 5);

        var ownerClient = _factory.CreateClient();
        UseBearerToken(ownerClient, await RegisterAndLoginTouristAsync(ownerClient, "res-get-other-owner"));
        var created = await (await ownerClient.PostAsJsonAsync("/api/reservations",
            new { experienceAvailabilityId = availability.Id, travelers = 1 }))
            .Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        var attackerClient = _factory.CreateClient();
        UseBearerToken(attackerClient, await RegisterAndLoginTouristAsync(attackerClient, "res-get-other-attacker"));

        var response = await attackerClient.GetAsync($"/api/reservations/{created!.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListMine_ReturnsOnlyOwnReservations()
    {
        var (_, _, availability) = await CreatePublishedExperienceWithAvailabilityAsync("res-list-mine", totalSlots: 5);

        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "res-list-mine-t"));
        await touristClient.PostAsJsonAsync("/api/reservations", new { experienceAvailabilityId = availability.Id, travelers = 1 });

        var otherClient = _factory.CreateClient();
        UseBearerToken(otherClient, await RegisterAndLoginTouristAsync(otherClient, "res-list-mine-other"));
        await otherClient.PostAsJsonAsync("/api/reservations", new { experienceAvailabilityId = availability.Id, travelers = 1 });

        var response = await touristClient.GetAsync("/api/reservations/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<ReservationResponse>>(JsonOptions);
        Assert.Single(body!.Items);
    }

    [Fact]
    public async Task CompanyReservations_List_ReturnsOnlyOwnCompanyItems()
    {
        var (providerClient, _, availability) = await CreatePublishedExperienceWithAvailabilityAsync("res-p12", totalSlots: 5);

        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "res-p12-t"));
        await touristClient.PostAsJsonAsync("/api/reservations", new { experienceAvailabilityId = availability.Id, travelers = 2 });

        var response = await providerClient.GetAsync("/api/companies/me/reservations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<ReservationItemResponse>>(JsonOptions);
        Assert.Single(body!.Items);
        Assert.Equal(2, body.Items[0].Travelers);
    }

    [Fact]
    public async Task CompanyReservations_GetById_AsAnotherProvider_Returns403()
    {
        var (_, _, availability) = await CreatePublishedExperienceWithAvailabilityAsync("res-p13-owner", totalSlots: 5);

        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "res-p13-t"));
        var reservation = await (await touristClient.PostAsJsonAsync("/api/reservations",
            new { experienceAvailabilityId = availability.Id, travelers = 1 }))
            .Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        var itemId = reservation!.Items[0].Id;

        var otherProviderClient = _factory.CreateClient();
        var otherProvider = await RegisterApprovedProviderAsync(otherProviderClient, _factory.CreateClient(), "res-p13-attacker");
        UseBearerToken(otherProviderClient, otherProvider.AccessToken);

        var response = await otherProviderClient.GetAsync($"/api/companies/me/reservations/{itemId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CompanyReservations_GetById_AsOwner_Returns200WithTouristInfo()
    {
        var (providerClient, _, availability) = await CreatePublishedExperienceWithAvailabilityAsync("res-p13-ok", totalSlots: 5);

        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "res-p13-ok-t"));
        var reservation = await (await touristClient.PostAsJsonAsync("/api/reservations",
            new { experienceAvailabilityId = availability.Id, travelers = 1 }))
            .Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        var itemId = reservation!.Items[0].Id;

        var response = await providerClient.GetAsync($"/api/companies/me/reservations/{itemId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReservationItemResponse>(JsonOptions);
        Assert.NotEqual(Guid.Empty, body!.TouristId);
        Assert.False(string.IsNullOrWhiteSpace(body.TouristName));
    }

    [Fact]
    public async Task Create_UnauthenticatedRequest_Returns401()
    {
        var anonymousClient = _factory.CreateClient();

        var response = await anonymousClient.PostAsJsonAsync("/api/reservations", new
        {
            experienceAvailabilityId = Guid.NewGuid(),
            travelers = 1
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_AsProvider_Returns403()
    {
        var (providerClient, _, availability) = await CreatePublishedExperienceWithAvailabilityAsync("res-provider-role", totalSlots: 5);

        var response = await providerClient.PostAsJsonAsync("/api/reservations", new
        {
            experienceAvailabilityId = availability.Id,
            travelers = 1
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
