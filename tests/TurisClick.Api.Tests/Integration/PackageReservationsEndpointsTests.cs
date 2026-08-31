using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Packages.Dtos;
using TurisClick.Api.Modules.Reservations.Dtos;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// UC-T-09/10, UC-P-12/13, UC-T-19 — reserva directa de un Package: transacción, prevención de
/// overbooking (incluyendo un escenario de concurrencia real contra Postgres) y pago (mismo flujo de
/// Oleada 3 que Experience, ejercitado acá con ProductType=PACKAGE).
/// </summary>
[Collection(ApiCollection.Name)]
public class PackageReservationsEndpointsTests
{
    private readonly TurisClickApiFactory _factory;

    public PackageReservationsEndpointsTests(TurisClickApiFactory factory) => _factory = factory;

    private async Task<(HttpClient ProviderClient, PackageResponse Package, PackageAvailabilityResponse Availability)>
        CreatePublishedPackageWithAvailabilityAsync(string emailPrefix, int totalSlots)
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
            price = 20m,
            currency = "USD"
        })).Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);

        var package = await (await providerClient.PostAsJsonAsync("/api/packages", new
        {
            title = $"Paquete-{suffix}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = city.Id,
            categoryIds = Array.Empty<Guid>(),
            durationDays = 2,
            price = 300m,
            currency = "USD",
            items = new object[] { new { dayNumber = 1, sortOrder = 0, kind = "EXPERIENCE_REFERENCE", experienceId = experience!.Id } },
            images = Array.Empty<object>()
        })).Content.ReadFromJsonAsync<PackageResponse>(JsonOptions);

        var availability = await (await providerClient.PostAsJsonAsync($"/api/packages/{package!.Id}/availability", new
        {
            departureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(12)),
            totalSlots
        })).Content.ReadFromJsonAsync<PackageAvailabilityResponse>(JsonOptions);

        var publishResponse = await providerClient.PostAsync($"/api/packages/{package.Id}/publish", null);
        publishResponse.EnsureSuccessStatusCode();

        return (providerClient, package, availability!);
    }

    [Fact]
    public async Task Create_ValidBooking_Returns201AndRetainsSlots()
    {
        var (_, package, availability) = await CreatePublishedPackageWithAvailabilityAsync("pkgres-create", totalSlots: 10);

        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "pkgres-tourist"));

        var response = await touristClient.PostAsJsonAsync("/api/reservations", new
        {
            packageAvailabilityId = availability.Id,
            travelers = 2
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        Assert.Equal("PENDING_PAYMENT", body!.Status);
        Assert.Single(body.Items);
        Assert.Equal("PACKAGE", body.Items[0].ProductType);
        Assert.Equal(package.Id, body.Items[0].PackageId);
        Assert.Equal(package.Title, body.Items[0].PackageTitle);
        Assert.Equal(2, body.Items[0].Travelers);
        Assert.Equal(600m, body.Items[0].Subtotal); // 300 * 2, precio congelado del servidor

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TurisClickDbContext>();
        var updated = await db.PackageAvailabilities.AsNoTracking().SingleAsync(a => a.Id == availability.Id);
        Assert.Equal(2, updated.ReservedSlots);
    }

    [Fact]
    public async Task Create_RequestWithBothIds_Returns400()
    {
        var (_, _, availability) = await CreatePublishedPackageWithAvailabilityAsync("pkgres-bothids", totalSlots: 5);

        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "pkgres-bothids-t"));

        var response = await touristClient.PostAsJsonAsync("/api/reservations", new
        {
            experienceAvailabilityId = Guid.NewGuid(),
            packageAvailabilityId = availability.Id,
            travelers = 1
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_RequestWithNeitherId_Returns400()
    {
        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "pkgres-noids-t"));

        var response = await touristClient.PostAsJsonAsync("/api/reservations", new { travelers = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_NonExistentPackageAvailability_Returns404()
    {
        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "pkgres-missing"));

        var response = await touristClient.PostAsJsonAsync("/api/reservations", new
        {
            packageAvailabilityId = Guid.NewGuid(),
            travelers = 1
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_UnpublishedPackage_Returns404()
    {
        var (providerClient, package, availability) = await CreatePublishedPackageWithAvailabilityAsync("pkgres-unpub", totalSlots: 5);
        await providerClient.PostAsync($"/api/packages/{package.Id}/unpublish", null);

        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "pkgres-unpub-t"));

        var response = await touristClient.PostAsJsonAsync("/api/reservations", new
        {
            packageAvailabilityId = availability.Id,
            travelers = 1
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_NotEnoughCapacity_Returns409()
    {
        var (_, _, availability) = await CreatePublishedPackageWithAvailabilityAsync("pkgres-nocap", totalSlots: 2);

        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "pkgres-nocap-t"));

        var response = await touristClient.PostAsJsonAsync("/api/reservations", new
        {
            packageAvailabilityId = availability.Id,
            travelers = 3
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_ClosedSlot_Returns410()
    {
        var (_, _, availability) = await CreatePublishedPackageWithAvailabilityAsync("pkgres-closed", totalSlots: 5);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TurisClickDbContext>();
            var entity = await db.PackageAvailabilities.SingleAsync(a => a.Id == availability.Id);
            entity.Status = AvailabilitySlotStatus.CLOSED;
            await db.SaveChangesAsync();
        }

        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "pkgres-closed-t"));

        var response = await touristClient.PostAsJsonAsync("/api/reservations", new
        {
            packageAvailabilityId = availability.Id,
            travelers = 1
        });

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task Create_DepartureDateInPast_Returns410()
    {
        var (_, _, availability) = await CreatePublishedPackageWithAvailabilityAsync("pkgres-pastdate", totalSlots: 5);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TurisClickDbContext>();
            var entity = await db.PackageAvailabilities.SingleAsync(a => a.Id == availability.Id);
            entity.DepartureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            await db.SaveChangesAsync();
        }

        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "pkgres-pastdate-t"));

        var response = await touristClient.PostAsJsonAsync("/api/reservations", new
        {
            packageAvailabilityId = availability.Id,
            travelers = 1
        });

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    /// <summary>
    /// UC-SYS-06 sobre PackageAvailability — mismo caso crítico que Experience (ReservationsEndpointsTests),
    /// ejercitado acá contra la tabla package_availabilities: dos reservas concurrentes compitiendo por el
    /// último cupo (TotalSlots = 1) contra Postgres real. Exactamente una debe ganar (201).
    /// </summary>
    [Fact]
    public async Task Create_TwoConcurrentBookingsForLastSlot_OnlyOneSucceeds()
    {
        var (_, _, availability) = await CreatePublishedPackageWithAvailabilityAsync("pkgres-race", totalSlots: 1);

        var touristAClient = _factory.CreateClient();
        UseBearerToken(touristAClient, await RegisterAndLoginTouristAsync(touristAClient, "pkgres-race-a"));
        var touristBClient = _factory.CreateClient();
        UseBearerToken(touristBClient, await RegisterAndLoginTouristAsync(touristBClient, "pkgres-race-b"));

        var payload = new { packageAvailabilityId = availability.Id, travelers = 1 };

        var taskA = touristAClient.PostAsJsonAsync("/api/reservations", payload);
        var taskB = touristBClient.PostAsJsonAsync("/api/reservations", payload);
        var results = await Task.WhenAll(taskA, taskB);

        var statusCodes = results.Select(r => r.StatusCode).ToList();
        Assert.Contains(HttpStatusCode.Created, statusCodes);
        Assert.Contains(HttpStatusCode.Conflict, statusCodes);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TurisClickDbContext>();
        var updated = await db.PackageAvailabilities.AsNoTracking().SingleAsync(a => a.Id == availability.Id);
        Assert.Equal(1, updated.ReservedSlots); // nunca 2 — sería overbooking
    }

    [Fact]
    public async Task CompanyReservations_List_ReturnsPackageItem()
    {
        var (providerClient, _, availability) = await CreatePublishedPackageWithAvailabilityAsync("pkgres-p12", totalSlots: 5);

        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "pkgres-p12-t"));
        await touristClient.PostAsJsonAsync("/api/reservations", new { packageAvailabilityId = availability.Id, travelers = 2 });

        var response = await providerClient.GetAsync("/api/companies/me/reservations");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<
            TurisClick.Api.Shared.Responses.PagedResult<ReservationItemResponse>>(JsonOptions);
        Assert.Single(body!.Items);
        Assert.Equal("PACKAGE", body.Items[0].ProductType);
        Assert.Equal(2, body.Items[0].Travelers);
    }

    // ---- UC-T-19 / UC-SYS-02 / UC-SYS-07 — pagar una reserva de Package (mismo flujo que Experience) ----

    private static async Task<ReservationResponse> CreateReservationAsync(HttpClient touristClient, Guid packageAvailabilityId, int travelers = 1)
    {
        var response = await touristClient.PostAsJsonAsync("/api/reservations",
            new { packageAvailabilityId, travelers });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions))!;
    }

    [Fact]
    public async Task Pay_Success_ConfirmsReservationAndItems()
    {
        var (_, _, availability) = await CreatePublishedPackageWithAvailabilityAsync("pkgpay-ok", totalSlots: 5);
        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "pkgpay-ok-t"));
        var reservation = await CreateReservationAsync(touristClient, availability.Id);

        var response = await touristClient.PostAsJsonAsync($"/api/reservations/{reservation.Id}/pay", new { success = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        Assert.Equal("CONFIRMED", body!.Status);
        Assert.NotNull(body.ConfirmedAt);
        Assert.True(body.PaymentApproved);
        Assert.All(body.Items, i => Assert.Equal("CONFIRMED", i.Status));
    }

    [Fact]
    public async Task Pay_PriceChanged_WithoutAcceptance_DoesNotChargeAndReturnsCurrentPrice()
    {
        var (providerClient, package, availability) = await CreatePublishedPackageWithAvailabilityAsync("pkgpay-price-block", totalSlots: 5);
        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "pkgpay-price-block-t"));
        var reservation = await CreateReservationAsync(touristClient, availability.Id, travelers: 2);
        var frozenSubtotal = reservation.Items[0].Subtotal;
        var newPrice = package.Price + 100m;

        var updateResponse = await providerClient.PutAsJsonAsync($"/api/packages/{package.Id}", new
        {
            title = package.Title,
            description = package.Description,
            destinationId = package.DestinationId,
            categoryIds = Array.Empty<Guid>(),
            durationDays = package.DurationDays,
            price = newPrice,
            currency = package.Currency,
            items = package.Items.Select(i => (object)new
            {
                dayNumber = i.DayNumber,
                sortOrder = i.SortOrder,
                kind = i.Kind,
                experienceId = i.ExperienceId,
                title = i.Kind == "DESCRIPTIVE" ? i.Title : null
            }),
            images = Array.Empty<object>()
        });
        updateResponse.EnsureSuccessStatusCode();

        var response = await touristClient.PostAsJsonAsync($"/api/reservations/{reservation.Id}/pay", new { success = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        Assert.True(body!.RequiresPriceAcceptance);
        Assert.Equal("PENDING_PAYMENT", body.Status); // no se cobró ni confirmó nada
        Assert.True(body.Items[0].PriceChanged);
        Assert.Equal(newPrice, body.Items[0].CurrentUnitPrice);
        Assert.Equal(frozenSubtotal, body.Items[0].Subtotal); // el congelado no se toca sin aceptación
    }

    [Fact]
    public async Task Pay_PriceChanged_WithAcceptance_ChargesNewPriceAndConfirms()
    {
        var (providerClient, package, availability) = await CreatePublishedPackageWithAvailabilityAsync("pkgpay-price-accept", totalSlots: 5);
        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, "pkgpay-price-accept-t"));
        var reservation = await CreateReservationAsync(touristClient, availability.Id, travelers: 2);
        var newPrice = package.Price + 100m;

        var updateResponse = await providerClient.PutAsJsonAsync($"/api/packages/{package.Id}", new
        {
            title = package.Title,
            description = package.Description,
            destinationId = package.DestinationId,
            categoryIds = Array.Empty<Guid>(),
            durationDays = package.DurationDays,
            price = newPrice,
            currency = package.Currency,
            items = package.Items.Select(i => (object)new
            {
                dayNumber = i.DayNumber,
                sortOrder = i.SortOrder,
                kind = i.Kind,
                experienceId = i.ExperienceId,
                title = i.Kind == "DESCRIPTIVE" ? i.Title : null
            }),
            images = Array.Empty<object>()
        });
        updateResponse.EnsureSuccessStatusCode();

        var response = await touristClient.PostAsJsonAsync($"/api/reservations/{reservation.Id}/pay",
            new { success = true, acceptPriceChanges = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        Assert.False(body!.RequiresPriceAcceptance);
        Assert.Equal("CONFIRMED", body.Status);
        Assert.True(body.PaymentApproved);
        Assert.Equal(newPrice, body.Items[0].UnitPrice);
        Assert.Equal(newPrice * 2, body.Items[0].Subtotal);
    }
}
