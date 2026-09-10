using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Ai.Dtos;
using TurisClick.Api.Modules.Ai.Entities;
using TurisClick.Api.Modules.Categories.Dtos;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Modules.Packages.Dtos;
using TurisClick.Api.Modules.Reservations.Dtos;
using TurisClick.Api.Modules.Reservations.Entities;
using TurisClick.Api.Modules.Reservations.Services;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// UC-SYS-08 (Oleada 8) contra PostgreSQL real. Acá se demuestra lo que no se puede demostrar con
/// mocks: que el cupo se libera de verdad, que liberarlo dos veces es imposible, y que pago y
/// expiración compiten de forma determinística por la misma reserva.
///
/// El timer está apagado en el entorno de tests (ver TurisClickApiFactory): la expiración se dispara
/// llamando al servicio, nunca esperando relojes reales.
/// </summary>
[Collection(ApiCollection.Name)]
public class ReservationExpirationTests
{
    private readonly TurisClickApiFactory _factory;

    public ReservationExpirationTests(TurisClickApiFactory factory) => _factory = factory;

    private static DateOnly AvailabilityDate => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));

    private sealed record Catalog(
        Guid CityId, string CityName,
        Guid ExperienceId, Guid ExperienceAvailabilityId,
        Guid PackageId, Guid PackageAvailabilityId,
        HttpClient ExperienceProvider, HttpClient PackageProvider,
        Guid ExperienceCompanyId, Guid PackageCompanyId);

    /// <summary>Dos proveedores y dos monedas, para que una sola reserva ejercite multi-provider y multi-moneda.</summary>
    private async Task<Catalog> SeedCatalogAsync(string prefix, int slots = 10)
    {
        var adminClient = _factory.CreateClient();
        UseBearerToken(adminClient, await LoginAsAdminAsync(adminClient));
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var country = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = $"PaisExp{suffix}", type = "COUNTRY" }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var region = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = $"RegionExp{suffix}", type = "REGION", parentId = country!.Id }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var cityName = $"CiudadExp{suffix}";
        var city = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = cityName, type = "CITY", parentId = region!.Id }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var category = await (await adminClient.PostAsJsonAsync("/api/admin/categories", new { name = $"InteresExp{suffix}" }))
            .Content.ReadFromJsonAsync<CategoryResponse>(JsonOptions);

        var expProvider = _factory.CreateClient();
        var expReg = await RegisterApprovedProviderAsync(expProvider, _factory.CreateClient(), $"{prefix}-exp");
        UseBearerToken(expProvider, expReg.AccessToken);

        var pkgProvider = _factory.CreateClient();
        var pkgReg = await RegisterApprovedProviderAsync(pkgProvider, _factory.CreateClient(), $"{prefix}-pkg");
        UseBearerToken(pkgProvider, pkgReg.AccessToken);

        var experience = await (await expProvider.PostAsJsonAsync("/api/experiences", new
        {
            title = $"Tour Exp {suffix}",
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
            title = $"Interno Exp {suffix}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = city.Id,
            categoryIds = Array.Empty<Guid>(),
            price = 10,
            currency = "BOB"
        })).Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);

        var package = await (await pkgProvider.PostAsJsonAsync("/api/packages", new
        {
            title = $"Paquete Exp {suffix}",
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

        return new Catalog(city.Id, cityName, experience.Id, expAvailability!.Id, package.Id, pkgAvailability!.Id,
            expProvider, pkgProvider, expReg.Company.Id, pkgReg.Company.Id);
    }

    // ---- Helpers de base y de dominio ----

    private async Task<T> QueryDbAsync<T>(Func<TurisClickDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<TurisClickDbContext>());
    }

    private Task<int> ExperienceSlotsAsync(Guid id) => QueryDbAsync(db => db.ExperienceAvailabilities
        .AsNoTracking().Where(a => a.Id == id).Select(a => a.ReservedSlots).FirstAsync());

    private Task<int> PackageSlotsAsync(Guid id) => QueryDbAsync(db => db.PackageAvailabilities
        .AsNoTracking().Where(a => a.Id == id).Select(a => a.ReservedSlots).FirstAsync());

    private Task<ReservationStatus> StatusOfAsync(Guid id) => QueryDbAsync(db => db.Reservations
        .AsNoTracking().Where(r => r.Id == id).Select(r => r.Status).FirstAsync());

    /// <summary>Fuerza el vencimiento del hold sin esperar 30 minutos reales.</summary>
    private Task ForceExpiryAsync(Guid reservationId) => QueryDbAsync(async db =>
        await db.Reservations.Where(r => r.Id == reservationId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));

    /// <summary>Invoca el servicio de expiración con un scope propio, como haría el BackgroundService.</summary>
    private async Task<T> WithExpirationServiceAsync<T>(Func<IReservationExpirationService, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IReservationExpirationService>());
    }

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

    /// <summary>Reserva multi-ítem/multi-proveedor a través del booking de itinerario IA (UC-T-18).</summary>
    private async Task<(HttpClient Tourist, Guid ItineraryId, ReservationResponse Reservation)> CreateItineraryReservationAsync(
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

        var itineraryId = generated!.Itinerary!.Id;

        var booked = await (await tourist.PostAsJsonAsync($"/api/ai/itineraries/{itineraryId}/book",
            new { acceptPriceChanges = true })).Content.ReadFromJsonAsync<BookItineraryResponse>(JsonOptions);

        return (tourist, itineraryId, booked!.Reservation!);
    }

    // ---- Expiración básica ----

    [Fact]
    public async Task Expire_PendingPaymentReservation_ReleasesCapacityAndMarksBothLevels()
    {
        var catalog = await SeedCatalogAsync("exp-basic");
        var (_, reservation) = await CreateDirectReservationAsync(catalog, "exp-basic-t");
        Assert.Equal(2, await ExperienceSlotsAsync(catalog.ExperienceAvailabilityId));

        await ForceExpiryAsync(reservation.Id);
        var expired = await WithExpirationServiceAsync(s => s.ExpireAsync(reservation.Id, CancellationToken.None));

        Assert.True(expired);
        Assert.Equal(0, await ExperienceSlotsAsync(catalog.ExperienceAvailabilityId));
        Assert.Equal(ReservationStatus.EXPIRED, await StatusOfAsync(reservation.Id));

        // Las líneas quedan EXPIRED, no CANCELLED: vencer y cancelar son eventos distintos.
        var itemStatuses = await QueryDbAsync(db => db.ReservationItems.AsNoTracking()
            .Where(i => i.ReservationId == reservation.Id).Select(i => i.Status).ToListAsync());
        Assert.All(itemStatuses, s => Assert.Equal(ReservationItemStatus.EXPIRED, s));
    }

    [Fact]
    public async Task Expire_MultiItemMultiProviderReservation_ReleasesEveryHold()
    {
        var catalog = await SeedCatalogAsync("exp-multi");
        var (_, _, reservation) = await CreateItineraryReservationAsync(catalog, "exp-multi-t");

        Assert.Equal(2, reservation.Items.Count);
        Assert.Equal(2, reservation.Items.Select(i => i.CompanyId).Distinct().Count()); // multi-provider
        Assert.Equal(2, reservation.Totals.Count);                                      // multi-moneda
        Assert.Equal(2, await ExperienceSlotsAsync(catalog.ExperienceAvailabilityId));
        Assert.Equal(2, await PackageSlotsAsync(catalog.PackageAvailabilityId));

        await ForceExpiryAsync(reservation.Id);
        Assert.True(await WithExpirationServiceAsync(s => s.ExpireAsync(reservation.Id, CancellationToken.None)));

        // Experience y Package, de empresas distintas y monedas distintas: se liberan los dos.
        Assert.Equal(0, await ExperienceSlotsAsync(catalog.ExperienceAvailabilityId));
        Assert.Equal(0, await PackageSlotsAsync(catalog.PackageAvailabilityId));
    }

    [Fact]
    public async Task Expire_RunTwice_ReleasesCapacityOnlyOnce()
    {
        var catalog = await SeedCatalogAsync("exp-idem");
        var (_, reservation) = await CreateDirectReservationAsync(catalog, "exp-idem-t");
        await ForceExpiryAsync(reservation.Id);

        Assert.True(await WithExpirationServiceAsync(s => s.ExpireAsync(reservation.Id, CancellationToken.None)));
        // La segunda pasada no gana la transición, así que no libera nada.
        Assert.False(await WithExpirationServiceAsync(s => s.ExpireAsync(reservation.Id, CancellationToken.None)));

        Assert.Equal(0, await ExperienceSlotsAsync(catalog.ExperienceAvailabilityId));
    }

    [Fact]
    public async Task Expire_ConcurrentDoubleExpiration_ReleasesCapacityOnlyOnce()
    {
        var catalog = await SeedCatalogAsync("exp-race", slots: 10);
        var (_, reservation) = await CreateDirectReservationAsync(catalog, "exp-race-t", travelers: 4);
        Assert.Equal(4, await ExperienceSlotsAsync(catalog.ExperienceAvailabilityId));
        await ForceExpiryAsync(reservation.Id);

        // Dos "instancias del backend" expirando la misma reserva al mismo tiempo.
        var results = await Task.WhenAll(
            WithExpirationServiceAsync(s => s.ExpireAsync(reservation.Id, CancellationToken.None)),
            WithExpirationServiceAsync(s => s.ExpireAsync(reservation.Id, CancellationToken.None)));

        Assert.Equal(1, results.Count(r => r));  // exactamente una gana la transición
        Assert.Equal(0, await ExperienceSlotsAsync(catalog.ExperienceAvailabilityId)); // 4 devueltos, no 8
    }

    [Fact]
    public async Task Expire_ConfirmedReservation_IsNeverExpired()
    {
        var catalog = await SeedCatalogAsync("exp-confirmed");
        var (tourist, reservation) = await CreateDirectReservationAsync(catalog, "exp-confirmed-t");

        var paid = await (await tourist.PostAsJsonAsync($"/api/reservations/{reservation.Id}/pay", new { success = true }))
            .Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        Assert.Equal("CONFIRMED", paid!.Status);

        // Aunque el hold "venza", una reserva pagada no se toca ni pierde su cupo.
        await ForceExpiryAsync(reservation.Id);
        Assert.False(await WithExpirationServiceAsync(s => s.ExpireAsync(reservation.Id, CancellationToken.None)));

        Assert.Equal(ReservationStatus.CONFIRMED, await StatusOfAsync(reservation.Id));
        Assert.Equal(2, await ExperienceSlotsAsync(catalog.ExperienceAvailabilityId));
    }

    [Fact]
    public async Task ExpireDueReservations_OnlyPicksUpTheOnesActuallyDue()
    {
        // La base de tests es persistente entre corridas, así que puede haber reservas vencidas de
        // ejecuciones anteriores. Se drenan primero para que el lote de esta prueba contenga solo lo suyo
        // (en producción el lote simplemente se vacía en pasadas sucesivas, que es el comportamiento correcto).
        while (await WithExpirationServiceAsync(s => s.ExpireDueReservationsAsync(500, CancellationToken.None)) > 0) { }

        var catalog = await SeedCatalogAsync("exp-batch", slots: 10);
        var (_, due) = await CreateDirectReservationAsync(catalog, "exp-batch-a", travelers: 2);
        var (_, notDue) = await CreateDirectReservationAsync(catalog, "exp-batch-b", travelers: 2);

        await ForceExpiryAsync(due.Id); // solo una vence

        await WithExpirationServiceAsync(s => s.ExpireDueReservationsAsync(100, CancellationToken.None));

        Assert.Equal(ReservationStatus.EXPIRED, await StatusOfAsync(due.Id));
        Assert.Equal(ReservationStatus.PENDING_PAYMENT, await StatusOfAsync(notDue.Id));
        Assert.Equal(2, await ExperienceSlotsAsync(catalog.ExperienceAvailabilityId)); // solo se liberaron los de la vencida
    }

    // ---- La carrera crítica: pago vs expiración ----

    [Fact]
    public async Task Pay_AfterExpiration_IsRejectedAndDoesNotConfirm()
    {
        var catalog = await SeedCatalogAsync("exp-pay-late");
        var (tourist, reservation) = await CreateDirectReservationAsync(catalog, "exp-pay-late-t");

        await ForceExpiryAsync(reservation.Id);
        Assert.True(await WithExpirationServiceAsync(s => s.ExpireAsync(reservation.Id, CancellationToken.None)));

        var response = await tourist.PostAsJsonAsync($"/api/reservations/{reservation.Id}/pay", new { success = true });

        // 410: el recurso existió pero su hold ya no vale. Nunca puede terminar CONFIRMED sin cupo.
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal(ReservationStatus.EXPIRED, await StatusOfAsync(reservation.Id));
        Assert.Equal(0, await ExperienceSlotsAsync(catalog.ExperienceAvailabilityId));
    }

    [Fact]
    public async Task Pay_AndExpire_Concurrently_ExactlyOneTransitionWins()
    {
        var catalog = await SeedCatalogAsync("exp-vs-pay");
        var (tourist, reservation) = await CreateDirectReservationAsync(catalog, "exp-vs-pay-t");
        await ForceExpiryAsync(reservation.Id);

        // Las dos operaciones compiten por la misma fila al mismo tiempo.
        var payTask = tourist.PostAsJsonAsync($"/api/reservations/{reservation.Id}/pay", new { success = true });
        var expireTask = WithExpirationServiceAsync(s => s.ExpireAsync(reservation.Id, CancellationToken.None));

        await Task.WhenAll(payTask, expireTask);

        var payResponse = await payTask;
        var expired = await expireTask;
        var finalStatus = await StatusOfAsync(reservation.Id);
        var slots = await ExperienceSlotsAsync(catalog.ExperienceAvailabilityId);

        if (expired)
        {
            // Ganó la expiración: el pago no pudo confirmar y el cupo volvió al catálogo.
            Assert.Equal(HttpStatusCode.Gone, payResponse.StatusCode);
            Assert.Equal(ReservationStatus.EXPIRED, finalStatus);
            Assert.Equal(0, slots);
        }
        else
        {
            // Ganó el pago: la reserva quedó confirmada y conserva su cupo retenido.
            Assert.Equal(HttpStatusCode.OK, payResponse.StatusCode);
            Assert.Equal(ReservationStatus.CONFIRMED, finalStatus);
            Assert.Equal(2, slots);
        }

        // Lo que nunca puede pasar: confirmada sin cupo, o expirada reteniendo cupo.
        Assert.False(finalStatus == ReservationStatus.CONFIRMED && slots == 0);
        Assert.False(finalStatus == ReservationStatus.EXPIRED && slots > 0);
    }

    // ---- AiItinerary tras la expiración ----

    [Fact]
    public async Task Expire_ItineraryReservation_ReturnsItineraryToSavedAndAllowsRebooking()
    {
        var catalog = await SeedCatalogAsync("exp-ai");
        var (tourist, itineraryId, reservation) = await CreateItineraryReservationAsync(catalog, "exp-ai-t");

        Assert.Equal(AiItineraryStatus.BOOKED, await QueryDbAsync(db => db.AiItineraries
            .AsNoTracking().Where(i => i.Id == itineraryId).Select(i => i.Status).FirstAsync()));

        await ForceExpiryAsync(reservation.Id);
        Assert.True(await WithExpirationServiceAsync(s => s.ExpireAsync(reservation.Id, CancellationToken.None)));

        // BOOKED vuelve a SAVED: la propuesta queda disponible para reintentar.
        var itinerary = await (await tourist.GetAsync($"/api/ai/itineraries/{itineraryId}"))
            .Content.ReadFromJsonAsync<ItineraryResponse>(JsonOptions);
        Assert.Equal("SAVED", itinerary!.Status);

        // Y se puede volver a reservar: el índice único ahora solo bloquea reservas ACTIVAS.
        var rebooked = await tourist.PostAsJsonAsync($"/api/ai/itineraries/{itineraryId}/book", new { acceptPriceChanges = true });
        Assert.Equal(HttpStatusCode.OK, rebooked.StatusCode);
        var body = await rebooked.Content.ReadFromJsonAsync<BookItineraryResponse>(JsonOptions);
        Assert.NotEqual(reservation.Id, body!.Reservation!.Id);

        // La reserva vieja se conserva para auditoría, con su vínculo intacto.
        var reservationsForItinerary = await QueryDbAsync(db => db.Reservations.AsNoTracking()
            .Where(r => r.AiItineraryId == itineraryId)
            .Select(r => r.Status).ToListAsync());
        Assert.Equal(2, reservationsForItinerary.Count);
        Assert.Contains(ReservationStatus.EXPIRED, reservationsForItinerary);
        Assert.Contains(ReservationStatus.PENDING_PAYMENT, reservationsForItinerary);
    }

    [Fact]
    public async Task Rebooking_AnExpiredItinerary_ConcurrentlyStillCreatesOnlyOneActiveReservation()
    {
        var catalog = await SeedCatalogAsync("exp-ai-race");
        var (tourist, itineraryId, reservation) = await CreateItineraryReservationAsync(catalog, "exp-ai-race-t");

        await ForceExpiryAsync(reservation.Id);
        await WithExpirationServiceAsync(s => s.ExpireAsync(reservation.Id, CancellationToken.None));

        // Dos intentos simultáneos de volver a reservar el mismo itinerario liberado.
        var responses = await Task.WhenAll(
            tourist.PostAsJsonAsync($"/api/ai/itineraries/{itineraryId}/book", new { acceptPriceChanges = true }),
            tourist.PostAsJsonAsync($"/api/ai/itineraries/{itineraryId}/book", new { acceptPriceChanges = true }));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));

        var activeCount = await QueryDbAsync(db => db.Reservations.AsNoTracking()
            .CountAsync(r => r.AiItineraryId == itineraryId
                && r.Status != ReservationStatus.EXPIRED && r.Status != ReservationStatus.CANCELLED));
        Assert.Equal(1, activeCount);
    }
}
