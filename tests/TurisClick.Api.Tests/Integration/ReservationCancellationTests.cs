using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Ai.Dtos;
using TurisClick.Api.Modules.Categories.Dtos;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Modules.Packages.Dtos;
using TurisClick.Api.Modules.Reservations.Dtos;
using TurisClick.Api.Modules.Reservations.Entities;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// UC-T-11 y UC-P-14 (Oleada 8) contra PostgreSQL real: cancelación total del turista y cancelación
/// parcial del proveedor sobre su propia línea, con liberación efectiva de cupo en ambos casos.
/// </summary>
[Collection(ApiCollection.Name)]
public class ReservationCancellationTests
{
    private readonly TurisClickApiFactory _factory;

    public ReservationCancellationTests(TurisClickApiFactory factory) => _factory = factory;

    private static DateOnly AvailabilityDate => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));

    private sealed record Catalog(
        string CityName,
        Guid ExperienceId, Guid ExperienceAvailabilityId,
        Guid PackageId, Guid PackageAvailabilityId,
        HttpClient ExperienceProvider, HttpClient PackageProvider);

    private async Task<Catalog> SeedCatalogAsync(string prefix, int slots = 10)
    {
        var adminClient = _factory.CreateClient();
        UseBearerToken(adminClient, await LoginAsAdminAsync(adminClient));
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var country = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = $"PaisCan{suffix}", type = "COUNTRY" }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var region = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = $"RegionCan{suffix}", type = "REGION", parentId = country!.Id }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var cityName = $"CiudadCan{suffix}";
        var city = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = cityName, type = "CITY", parentId = region!.Id }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var category = await (await adminClient.PostAsJsonAsync("/api/admin/categories", new { name = $"InteresCan{suffix}" }))
            .Content.ReadFromJsonAsync<CategoryResponse>(JsonOptions);

        var expProvider = _factory.CreateClient();
        UseBearerToken(expProvider, (await RegisterApprovedProviderAsync(expProvider, _factory.CreateClient(), $"{prefix}-exp")).AccessToken);

        var pkgProvider = _factory.CreateClient();
        UseBearerToken(pkgProvider, (await RegisterApprovedProviderAsync(pkgProvider, _factory.CreateClient(), $"{prefix}-pkg")).AccessToken);

        var experience = await (await expProvider.PostAsJsonAsync("/api/experiences", new
        {
            title = $"Tour Can {suffix}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = city!.Id,
            categoryIds = new[] { category!.Id },
            price = 40,
            currency = "USD"
        })).Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);

        var expAvailability = await (await expProvider.PostAsJsonAsync($"/api/experiences/{experience!.Id}/availability", new
        {
            date = AvailabilityDate,
            totalSlots = slots
        })).Content.ReadFromJsonAsync<ExperienceAvailabilityResponse>(JsonOptions);
        await expProvider.PostAsync($"/api/experiences/{experience.Id}/publish", null);

        var inner = await (await pkgProvider.PostAsJsonAsync("/api/experiences", new
        {
            title = $"Interno Can {suffix}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = city.Id,
            categoryIds = Array.Empty<Guid>(),
            price = 10,
            currency = "BOB"
        })).Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);

        var package = await (await pkgProvider.PostAsJsonAsync("/api/packages", new
        {
            title = $"Paquete Can {suffix}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = city.Id,
            categoryIds = new[] { category.Id },
            durationDays = 1,
            price = 300,
            currency = "BOB",
            items = new object[] { new { dayNumber = 1, sortOrder = 0, kind = "EXPERIENCE_REFERENCE", experienceId = inner!.Id } },
            images = Array.Empty<object>()
        })).Content.ReadFromJsonAsync<PackageResponse>(JsonOptions);

        var pkgAvailability = await (await pkgProvider.PostAsJsonAsync($"/api/packages/{package!.Id}/availability", new
        {
            departureDate = AvailabilityDate,
            totalSlots = slots
        })).Content.ReadFromJsonAsync<PackageAvailabilityResponse>(JsonOptions);
        await pkgProvider.PostAsync($"/api/packages/{package.Id}/publish", null);

        return new Catalog(cityName, experience.Id, expAvailability!.Id, package.Id, pkgAvailability!.Id, expProvider, pkgProvider);
    }

    private async Task<T> QueryDbAsync<T>(Func<TurisClickDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<TurisClickDbContext>());
    }

    private Task<int> ExperienceSlotsAsync(Guid id) => QueryDbAsync(db => db.ExperienceAvailabilities
        .AsNoTracking().Where(a => a.Id == id).Select(a => a.ReservedSlots).FirstAsync());

    private Task<int> PackageSlotsAsync(Guid id) => QueryDbAsync(db => db.PackageAvailabilities
        .AsNoTracking().Where(a => a.Id == id).Select(a => a.ReservedSlots).FirstAsync());

    private async Task<(HttpClient Tourist, ReservationResponse Reservation)> CreateDirectReservationAsync(
        Catalog catalog, string prefix, int travelers = 2)
    {
        var tourist = _factory.CreateClient();
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, prefix));

        var reservation = await (await tourist.PostAsJsonAsync("/api/reservations",
            new { experienceAvailabilityId = catalog.ExperienceAvailabilityId, travelers }))
            .Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        return (tourist, reservation!);
    }

    private async Task<(HttpClient Tourist, ReservationResponse Reservation)> CreateItineraryReservationAsync(
        Catalog catalog, string prefix)
    {
        var tourist = _factory.CreateClient();
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, prefix));

        var conversation = await (await tourist.PostAsync("/api/ai/conversations", null))
            .Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions);

        var from = AvailabilityDate;
        var to = AvailabilityDate.AddDays(1);
        var generated = await (await tourist.PostAsJsonAsync($"/api/ai/conversations/{conversation!.Id}/messages",
            new { content = $"Quiero ir a {catalog.CityName} del {from:yyyy-MM-dd} al {to:yyyy-MM-dd}, somos 2 personas." }))
            .Content.ReadFromJsonAsync<SendMessageResponse>(JsonOptions);

        var booked = await (await tourist.PostAsJsonAsync($"/api/ai/itineraries/{generated!.Itinerary!.Id}/book",
            new { acceptPriceChanges = true })).Content.ReadFromJsonAsync<BookItineraryResponse>(JsonOptions);

        return (tourist, booked!.Reservation!);
    }

    // ---- UC-T-11: cancelación del turista ----

    [Fact]
    public async Task Cancel_PendingPaymentReservation_ReleasesCapacity()
    {
        var catalog = await SeedCatalogAsync("can-ok");
        var (tourist, reservation) = await CreateDirectReservationAsync(catalog, "can-ok-t");
        Assert.Equal(2, await ExperienceSlotsAsync(catalog.ExperienceAvailabilityId));

        var response = await tourist.PostAsync($"/api/reservations/{reservation.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        Assert.Equal("CANCELLED", body!.Status);
        Assert.NotNull(body.CancelledAt);
        Assert.All(body.Items, i => Assert.Equal("CANCELLED", i.Status));

        Assert.Equal(0, await ExperienceSlotsAsync(catalog.ExperienceAvailabilityId));
    }

    [Fact]
    public async Task Cancel_MultiProviderReservation_ReleasesEveryHold()
    {
        var catalog = await SeedCatalogAsync("can-multi");
        var (tourist, reservation) = await CreateItineraryReservationAsync(catalog, "can-multi-t");
        Assert.Equal(2, reservation.Items.Count);

        var response = await tourist.PostAsync($"/api/reservations/{reservation.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await ExperienceSlotsAsync(catalog.ExperienceAvailabilityId));
        Assert.Equal(0, await PackageSlotsAsync(catalog.PackageAvailabilityId));
    }

    [Fact]
    public async Task Cancel_Twice_DoesNotReleaseCapacityTwice()
    {
        var catalog = await SeedCatalogAsync("can-idem");
        var (tourist, reservation) = await CreateDirectReservationAsync(catalog, "can-idem-t");

        Assert.Equal(HttpStatusCode.OK, (await tourist.PostAsync($"/api/reservations/{reservation.Id}/cancel", null)).StatusCode);
        var second = await tourist.PostAsync($"/api/reservations/{reservation.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(0, await ExperienceSlotsAsync(catalog.ExperienceAvailabilityId));
    }

    [Fact]
    public async Task Cancel_ConfirmedReservation_IsRejectedBecauseRefundPolicyDoesNotExistYet()
    {
        var catalog = await SeedCatalogAsync("can-confirmed");
        var (tourist, reservation) = await CreateDirectReservationAsync(catalog, "can-confirmed-t");
        await tourist.PostAsJsonAsync($"/api/reservations/{reservation.Id}/pay", new { success = true });

        var response = await tourist.PostAsync($"/api/reservations/{reservation.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("REFUND_POLICY_REQUIRED", await response.Content.ReadAsStringAsync());
        // El cupo sigue retenido: la reserva pagada no se tocó.
        Assert.Equal(2, await ExperienceSlotsAsync(catalog.ExperienceAvailabilityId));
    }

    [Fact]
    public async Task Cancel_AnotherTouristsReservation_IsForbidden()
    {
        var catalog = await SeedCatalogAsync("can-own");
        var (_, reservation) = await CreateDirectReservationAsync(catalog, "can-own-a");

        var intruder = _factory.CreateClient();
        UseBearerToken(intruder, await RegisterAndLoginTouristAsync(intruder, "can-own-b"));

        var response = await intruder.PostAsync($"/api/reservations/{reservation.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(2, await ExperienceSlotsAsync(catalog.ExperienceAvailabilityId));
    }

    // ---- UC-P-14: cancelación parcial del proveedor ----

    [Fact]
    public async Task ProviderCancel_OnlyItsOwnLine_KeepsTheParentReservationActive()
    {
        var catalog = await SeedCatalogAsync("can-prov");
        var (tourist, reservation) = await CreateItineraryReservationAsync(catalog, "can-prov-t");

        var experienceItem = reservation.Items.Single(i => i.ExperienceId == catalog.ExperienceId);
        var packageItem = reservation.Items.Single(i => i.PackageId == catalog.PackageId);

        var response = await catalog.ExperienceProvider.PostAsJsonAsync(
            $"/api/companies/me/reservations/{experienceItem.Id}/cancel",
            new { reason = "Cierre imprevisto del acceso al salar por obras." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReservationItemResponse>(JsonOptions);
        Assert.Equal("CANCELLED", body!.Status);
        Assert.NotNull(body.CancelledAt);
        Assert.Equal("Cierre imprevisto del acceso al salar por obras.", body.CancellationReason);

        // Solo se liberó SU cupo; el del otro proveedor sigue retenido.
        Assert.Equal(0, await ExperienceSlotsAsync(catalog.ExperienceAvailabilityId));
        Assert.Equal(2, await PackageSlotsAsync(catalog.PackageAvailabilityId));

        // La reserva padre sigue activa y la otra línea intacta (no existe PARTIALLY_CANCELLED: se deriva).
        var parent = await (await tourist.GetAsync($"/api/reservations/{reservation.Id}"))
            .Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        Assert.Equal("PENDING_PAYMENT", parent!.Status);
        Assert.Equal("CANCELLED", parent.Items.Single(i => i.Id == experienceItem.Id).Status);
        Assert.Equal("PENDING_PAYMENT", parent.Items.Single(i => i.Id == packageItem.Id).Status);
    }

    [Fact]
    public async Task ProviderCancel_TwiceOnTheSameLine_DoesNotReleaseCapacityTwice()
    {
        var catalog = await SeedCatalogAsync("can-prov-idem");
        var (_, reservation) = await CreateItineraryReservationAsync(catalog, "can-prov-idem-t");
        var item = reservation.Items.Single(i => i.ExperienceId == catalog.ExperienceId);

        var payload = new { reason = "Motivo de fuerza mayor documentado." };
        Assert.Equal(HttpStatusCode.OK,
            (await catalog.ExperienceProvider.PostAsJsonAsync($"/api/companies/me/reservations/{item.Id}/cancel", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await catalog.ExperienceProvider.PostAsJsonAsync($"/api/companies/me/reservations/{item.Id}/cancel", payload)).StatusCode);

        Assert.Equal(0, await ExperienceSlotsAsync(catalog.ExperienceAvailabilityId));
    }

    [Fact]
    public async Task ProviderCancel_AnotherProvidersLine_IsForbidden()
    {
        var catalog = await SeedCatalogAsync("can-prov-own");
        var (_, reservation) = await CreateItineraryReservationAsync(catalog, "can-prov-own-t");
        var packageItem = reservation.Items.Single(i => i.PackageId == catalog.PackageId);

        // El proveedor de la Experience intenta cancelar la línea del Package (otra empresa).
        var response = await catalog.ExperienceProvider.PostAsJsonAsync(
            $"/api/companies/me/reservations/{packageItem.Id}/cancel",
            new { reason = "Intento de cancelar una línea ajena." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(2, await PackageSlotsAsync(catalog.PackageAvailabilityId));
    }

    [Fact]
    public async Task ProviderCancel_WithoutReason_IsRejected()
    {
        var catalog = await SeedCatalogAsync("can-prov-reason");
        var (_, reservation) = await CreateItineraryReservationAsync(catalog, "can-prov-reason-t");
        var item = reservation.Items.Single(i => i.ExperienceId == catalog.ExperienceId);

        // El motivo es obligatorio: es una cancelación excepcional y tiene que quedar registrada.
        var response = await catalog.ExperienceProvider.PostAsJsonAsync(
            $"/api/companies/me/reservations/{item.Id}/cancel", new { reason = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(2, await ExperienceSlotsAsync(catalog.ExperienceAvailabilityId));
    }

    [Fact]
    public async Task TouristCancel_AfterProviderCancelledOneLine_ReleasesOnlyWhatWasStillHeld()
    {
        var catalog = await SeedCatalogAsync("can-mixed");
        var (tourist, reservation) = await CreateItineraryReservationAsync(catalog, "can-mixed-t");
        var experienceItem = reservation.Items.Single(i => i.ExperienceId == catalog.ExperienceId);

        await catalog.ExperienceProvider.PostAsJsonAsync(
            $"/api/companies/me/reservations/{experienceItem.Id}/cancel",
            new { reason = "El proveedor cancela su parte primero." });

        var response = await tourist.PostAsync($"/api/reservations/{reservation.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Ninguna capacidad se libera dos veces: la línea ya cancelada no vuelve a devolver cupo.
        Assert.Equal(0, await ExperienceSlotsAsync(catalog.ExperienceAvailabilityId));
        Assert.Equal(0, await PackageSlotsAsync(catalog.PackageAvailabilityId));

        // Y la línea del proveedor conserva su motivo original.
        var reason = await QueryDbAsync(db => db.ReservationItems.AsNoTracking()
            .Where(i => i.Id == experienceItem.Id).Select(i => i.CancellationReason).FirstAsync());
        Assert.Equal("El proveedor cancela su parte primero.", reason);
    }
}
