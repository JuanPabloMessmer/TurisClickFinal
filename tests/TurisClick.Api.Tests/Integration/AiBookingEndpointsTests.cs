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
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Packages.Dtos;
using TurisClick.Api.Modules.Reservations.Dtos;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// UC-T-18 / UC-SYS-05/06 (Oleada 7) contra PostgreSQL real. Acá se prueba lo que no se puede probar
/// con mocks: holds atómicos, rollback total ante un fallo intermedio, doble booking concurrente y dos
/// turistas compitiendo por el último cupo.
/// </summary>
[Collection(ApiCollection.Name)]
public class AiBookingEndpointsTests
{
    private readonly TurisClickApiFactory _factory;

    public AiBookingEndpointsTests(TurisClickApiFactory factory) => _factory = factory;

    private static DateOnly AvailabilityDate => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));

    private sealed record Catalog(
        Guid CityId,
        string CityName,
        Guid ExperienceId,
        Guid ExperienceAvailabilityId,
        Guid PackageId,
        Guid PackageAvailabilityId,
        HttpClient ExperienceProvider,
        HttpClient PackageProvider);

    /// <summary>
    /// Catálogo con DOS proveedores distintos (uno vende la Experience, otro el Package) y dos monedas,
    /// para que un mismo itinerario ejercite multi-provider y multi-moneda a la vez.
    /// </summary>
    private async Task<Catalog> SeedCatalogAsync(string prefix, int experienceSlots = 10, int packageSlots = 10)
    {
        var adminClient = _factory.CreateClient();
        UseBearerToken(adminClient, await LoginAsAdminAsync(adminClient));
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var country = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = $"PaisBook{suffix}", type = "COUNTRY" }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var region = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = $"RegionBook{suffix}", type = "REGION", parentId = country!.Id }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var cityName = $"CiudadBook{suffix}";
        var city = await (await adminClient.PostAsJsonAsync("/api/admin/destinations", new { name = cityName, type = "CITY", parentId = region!.Id }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);

        var category = await (await adminClient.PostAsJsonAsync("/api/admin/categories", new { name = $"InteresBook{suffix}" }))
            .Content.ReadFromJsonAsync<CategoryResponse>(JsonOptions);

        var expProvider = _factory.CreateClient();
        UseBearerToken(expProvider, (await RegisterApprovedProviderAsync(expProvider, _factory.CreateClient(), $"{prefix}-exp")).AccessToken);

        var pkgProvider = _factory.CreateClient();
        UseBearerToken(pkgProvider, (await RegisterApprovedProviderAsync(pkgProvider, _factory.CreateClient(), $"{prefix}-pkg")).AccessToken);

        // Experience (USD) del proveedor 1.
        var experience = await (await expProvider.PostAsJsonAsync("/api/experiences", new
        {
            title = $"Tour Book {suffix}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = city!.Id,
            categoryIds = new[] { category!.Id },
            price = 40,
            currency = "USD"
        })).Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);

        var expAvailability = await (await expProvider.PostAsJsonAsync($"/api/experiences/{experience!.Id}/availability", new
        {
            date = AvailabilityDate,
            totalSlots = experienceSlots
        })).Content.ReadFromJsonAsync<ExperienceAvailabilityResponse>(JsonOptions);
        await expProvider.PostAsync($"/api/experiences/{experience.Id}/publish", null);

        // Package (BOB) del proveedor 2 — otra empresa y otra moneda.
        var pkgExperience = await (await pkgProvider.PostAsJsonAsync("/api/experiences", new
        {
            title = $"Interno Book {suffix}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = city.Id,
            categoryIds = Array.Empty<Guid>(),
            price = 10,
            currency = "BOB"
        })).Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);

        var package = await (await pkgProvider.PostAsJsonAsync("/api/packages", new
        {
            title = $"Paquete Book {suffix}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = city.Id,
            categoryIds = new[] { category.Id },
            durationDays = 1,
            price = 300,
            currency = "BOB",
            items = new object[] { new { dayNumber = 1, sortOrder = 0, kind = "EXPERIENCE_REFERENCE", experienceId = pkgExperience!.Id } },
            images = Array.Empty<object>()
        })).Content.ReadFromJsonAsync<PackageResponse>(JsonOptions);

        var pkgAvailability = await (await pkgProvider.PostAsJsonAsync($"/api/packages/{package!.Id}/availability", new
        {
            departureDate = AvailabilityDate,
            totalSlots = packageSlots
        })).Content.ReadFromJsonAsync<PackageAvailabilityResponse>(JsonOptions);
        await pkgProvider.PostAsync($"/api/packages/{package.Id}/publish", null);

        return new Catalog(city.Id, cityName, experience.Id, expAvailability!.Id, package.Id, pkgAvailability!.Id, expProvider, pkgProvider);
    }

    private async Task<(HttpClient Tourist, ItineraryResponse Itinerary)> GenerateItineraryAsync(Catalog catalog, string touristPrefix)
    {
        var tourist = _factory.CreateClient();
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, touristPrefix));

        var conversation = await (await tourist.PostAsync("/api/ai/conversations", null))
            .Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions);

        // Ventana de 2 días: un Package de 1 día solo califica como "strong fit" cuando la duración del
        // viaje se le parece (ver RetrievalService.ScorePackage), y es eso lo que hace que la propuesta
        // combine Package + Experience — o sea, dos proveedores y dos monedas en el mismo itinerario.
        var from = AvailabilityDate;
        var to = AvailabilityDate.AddDays(1);
        var generated = await (await tourist.PostAsJsonAsync($"/api/ai/conversations/{conversation!.Id}/messages",
            new { content = $"Quiero ir a {catalog.CityName} del {from:yyyy-MM-dd} al {to:yyyy-MM-dd}, somos 2 personas." }))
            .Content.ReadFromJsonAsync<SendMessageResponse>(JsonOptions);

        Assert.NotNull(generated!.Itinerary);
        return (tourist, generated.Itinerary!);
    }

    private static Task<HttpResponseMessage> BookAsync(HttpClient client, Guid itineraryId, bool acceptPriceChanges = false) =>
        client.PostAsJsonAsync($"/api/ai/itineraries/{itineraryId}/book", new { acceptPriceChanges });

    private async Task<T> QueryDbAsync<T>(Func<TurisClickDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<TurisClickDbContext>());
    }

    private Task<int> ReservedSlotsOfExperienceAsync(Guid availabilityId) =>
        QueryDbAsync(db => db.ExperienceAvailabilities.AsNoTracking()
            .Where(a => a.Id == availabilityId).Select(a => a.ReservedSlots).FirstAsync());

    private Task<int> ReservedSlotsOfPackageAsync(Guid availabilityId) =>
        QueryDbAsync(db => db.PackageAvailabilities.AsNoTracking()
            .Where(a => a.Id == availabilityId).Select(a => a.ReservedSlots).FirstAsync());

    // ---- Camino feliz ----

    [Fact]
    public async Task Book_MultiProviderMultiCurrencyItinerary_CreatesOneReservationPendingPayment()
    {
        var catalog = await SeedCatalogAsync("book-ok");
        var (tourist, itinerary) = await GenerateItineraryAsync(catalog, "book-ok-t");
        Assert.Equal(2, itinerary.Items.Count); // Experience (USD) + Package (BOB)

        var response = await BookAsync(tourist, itinerary.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BookItineraryResponse>(JsonOptions);

        Assert.False(body!.RequiresPriceAcceptance);
        var reservation = body.Reservation;
        Assert.NotNull(reservation);
        Assert.Equal("PENDING_PAYMENT", reservation!.Status);       // no cobra automáticamente
        Assert.NotNull(reservation.ExpiresAt);                       // misma política de hold que una reserva directa
        Assert.Equal(2, reservation.Items.Count);

        // Multi-provider: dos empresas distintas en la MISMA reserva.
        Assert.Equal(2, reservation.Items.Select(i => i.CompanyId).Distinct().Count());
        // Multi-moneda: un subtotal por moneda, sin ninguna suma cruzada.
        Assert.Equal(2, reservation.Totals.Count);
        Assert.Equal(80, reservation.Totals.Single(t => t.Currency == "USD").Amount);   // 40 × 2
        Assert.Equal(600, reservation.Totals.Single(t => t.Currency == "BOB").Amount);  // 300 × 2

        // El cupo se tomó de verdad en ambos productos.
        Assert.Equal(2, await ReservedSlotsOfExperienceAsync(catalog.ExperienceAvailabilityId));
        Assert.Equal(2, await ReservedSlotsOfPackageAsync(catalog.PackageAvailabilityId));

        // El itinerario quedó BOOKED y sabe cuál es su reserva.
        var itineraryAfter = await (await tourist.GetAsync($"/api/ai/itineraries/{itinerary.Id}"))
            .Content.ReadFromJsonAsync<ItineraryResponse>(JsonOptions);
        Assert.Equal("BOOKED", itineraryAfter!.Status);

        var linked = await QueryDbAsync(db => db.Reservations.AsNoTracking()
            .Where(r => r.AiItineraryId == itinerary.Id).Select(r => r.Id).ToListAsync());
        Assert.Equal(reservation.Id, Assert.Single(linked));
    }

    [Fact]
    public async Task Book_ReservationItemsSnapshotCurrentPriceAndKeepDayNumber()
    {
        var catalog = await SeedCatalogAsync("book-snap");
        var (tourist, itinerary) = await GenerateItineraryAsync(catalog, "book-snap-t");

        var body = await (await BookAsync(tourist, itinerary.Id)).Content.ReadFromJsonAsync<BookItineraryResponse>(JsonOptions);

        var experienceItem = Assert.Single(body!.Reservation!.Items, i => i.ExperienceId == catalog.ExperienceId);
        Assert.Equal(40, experienceItem.UnitPrice);
        Assert.Equal("USD", experienceItem.Currency);
        Assert.Equal(80, experienceItem.Subtotal);      // 40 × 2 viajeros
        Assert.Equal(2, experienceItem.Travelers);
    }

    // ---- Estados y ownership ----

    [Fact]
    public async Task Book_Twice_SequentiallyIsRejectedWithExistingReservation()
    {
        var catalog = await SeedCatalogAsync("book-twice");
        var (tourist, itinerary) = await GenerateItineraryAsync(catalog, "book-twice-t");

        var first = await BookAsync(tourist, itinerary.Id);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await BookAsync(tourist, itinerary.Id);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("ITINERARY_ALREADY_BOOKED", await second.Content.ReadAsStringAsync());

        // No se duplicó ni la reserva ni el cupo.
        var reservationCount = await QueryDbAsync(db => db.Reservations.AsNoTracking().CountAsync(r => r.AiItineraryId == itinerary.Id));
        Assert.Equal(1, reservationCount);
        Assert.Equal(2, await ReservedSlotsOfExperienceAsync(catalog.ExperienceAvailabilityId));
    }

    [Fact]
    public async Task Book_ConcurrentDoubleSubmit_CreatesExactlyOneReservation()
    {
        // El caso que un `if (status != BOOKED)` en C# NO cubre: los dos requests leen el itinerario
        // como reservable antes de que ninguno escriba. La garantía es el índice único parcial sobre
        // reservations.ai_itinerary_id (migración 0007).
        var catalog = await SeedCatalogAsync("book-race");
        var (tourist, itinerary) = await GenerateItineraryAsync(catalog, "book-race-t");

        var responses = await Task.WhenAll(
            BookAsync(tourist, itinerary.Id),
            BookAsync(tourist, itinerary.Id));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var reservationCount = await QueryDbAsync(db => db.Reservations.AsNoTracking().CountAsync(r => r.AiItineraryId == itinerary.Id));
        Assert.Equal(1, reservationCount);
        // Y el cupo se tomó UNA sola vez: 2 viajeros, no 4.
        Assert.Equal(2, await ReservedSlotsOfExperienceAsync(catalog.ExperienceAvailabilityId));
        Assert.Equal(2, await ReservedSlotsOfPackageAsync(catalog.PackageAvailabilityId));
    }

    [Fact]
    public async Task Book_AnotherTourist_IsForbiddenAndHoldsNothing()
    {
        var catalog = await SeedCatalogAsync("book-own");
        var (_, itinerary) = await GenerateItineraryAsync(catalog, "book-own-a");

        var intruder = _factory.CreateClient();
        UseBearerToken(intruder, await RegisterAndLoginTouristAsync(intruder, "book-own-b"));

        var response = await BookAsync(intruder, itinerary.Id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await ReservedSlotsOfExperienceAsync(catalog.ExperienceAvailabilityId));
    }

    [Fact]
    public async Task Book_AsProvider_IsForbidden()
    {
        var catalog = await SeedCatalogAsync("book-prov");
        var (_, itinerary) = await GenerateItineraryAsync(catalog, "book-prov-t");

        // El endpoint es exclusivo de TOURIST.
        var response = await BookAsync(catalog.ExperienceProvider, itinerary.Id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- Atomicidad: todo o nada ----

    [Fact]
    public async Task Book_WhenOneItemIsUnpublished_RollsBackEverything()
    {
        var catalog = await SeedCatalogAsync("book-unpub");
        var (tourist, itinerary) = await GenerateItineraryAsync(catalog, "book-unpub-t");

        // El proveedor del Package despublica DESPUÉS de que se generó la propuesta.
        await catalog.PackageProvider.PostAsync($"/api/packages/{catalog.PackageId}/unpublish", null);

        var response = await BookAsync(tourist, itinerary.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("PRODUCT_UNAVAILABLE", await response.Content.ReadAsStringAsync());

        // Ni un solo cupo tomado, ni reserva, ni itinerario BOOKED.
        Assert.Equal(0, await ReservedSlotsOfExperienceAsync(catalog.ExperienceAvailabilityId));
        Assert.Equal(0, await ReservedSlotsOfPackageAsync(catalog.PackageAvailabilityId));
        Assert.Equal(0, await QueryDbAsync(db => db.Reservations.AsNoTracking().CountAsync(r => r.AiItineraryId == itinerary.Id)));
        Assert.Equal(AiItineraryStatus.DRAFT, await QueryDbAsync(db => db.AiItineraries.AsNoTracking()
            .Where(i => i.Id == itinerary.Id).Select(i => i.Status).FirstAsync()));
    }

    [Fact]
    public async Task Book_WhenOneItemIsSoldOut_HoldsNothingAtAll()
    {
        // El Package se queda sin cupo entre la propuesta y el booking. Acá la revalidación previa ya
        // lo detecta, así que ni siquiera se llega a tomar el primer hold.
        var catalog = await SeedCatalogAsync("book-partial", experienceSlots: 10, packageSlots: 2);
        var (tourist, itinerary) = await GenerateItineraryAsync(catalog, "book-partial-t");

        // Otro turista consume los 2 cupos del Package con una reserva directa.
        var competitor = _factory.CreateClient();
        UseBearerToken(competitor, await RegisterAndLoginTouristAsync(competitor, "book-partial-c"));
        var competing = await competitor.PostAsJsonAsync("/api/reservations",
            new { packageAvailabilityId = catalog.PackageAvailabilityId, travelers = 2 });
        Assert.Equal(HttpStatusCode.Created, competing.StatusCode);

        var response = await BookAsync(tourist, itinerary.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // La Experience volvió a su valor original: no quedó un hold huérfano.
        Assert.Equal(0, await ReservedSlotsOfExperienceAsync(catalog.ExperienceAvailabilityId));
        // El Package sigue con los 2 cupos del competidor, ni uno más.
        Assert.Equal(2, await ReservedSlotsOfPackageAsync(catalog.PackageAvailabilityId));
        Assert.Equal(0, await QueryDbAsync(db => db.Reservations.AsNoTracking().CountAsync(r => r.AiItineraryId == itinerary.Id)));
        Assert.Equal(0, await QueryDbAsync(db => db.ReservationItems.AsNoTracking()
            .CountAsync(i => i.Reservation!.AiItineraryId == itinerary.Id)));
    }

    [Fact]
    public async Task Book_WhenTheSecondHoldFails_TheFirstHoldIsRolledBack()
    {
        // Caso que la revalidación previa NO puede detectar mirando ítem por ítem: dos componentes del
        // itinerario apuntan al MISMO slot. Cada uno cabe por separado (2 ≤ 2), pero juntos necesitan 4
        // cupos de 2. El hold agrupado falla DESPUÉS de que el hold de la Experience ya se tomó, así que
        // este test es el que demuestra el rollback real de un hold intermedio (y de paso, que agrupar
        // por availability es lo correcto).
        var catalog = await SeedCatalogAsync("book-rollback", experienceSlots: 10, packageSlots: 2);
        var (tourist, itinerary) = await GenerateItineraryAsync(catalog, "book-rollback-t");

        var original = itinerary.Items.Single(i => i.PackageId == catalog.PackageId);
        await QueryDbAsync(async db =>
        {
            db.AiItineraryItems.Add(new AiItineraryItem
            {
                Id = Guid.NewGuid(),
                AiItineraryId = itinerary.Id,
                DayNumber = 3,
                SortOrder = 0,
                ProductType = TurisClick.Api.Modules.Reservations.Entities.ProductType.PACKAGE,
                PackageId = catalog.PackageId,
                PackageAvailabilityId = catalog.PackageAvailabilityId,
                EstimatedUnitPrice = original.EstimatedUnitPrice,
                Currency = original.Currency
            });
            return await db.SaveChangesAsync();
        });

        var response = await BookAsync(tourist, itinerary.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("INSUFFICIENT_CAPACITY", await response.Content.ReadAsStringAsync());

        // Lo importante: la Experience SÍ llegó a tomarse dentro de la transacción y volvió a 0.
        Assert.Equal(0, await ReservedSlotsOfExperienceAsync(catalog.ExperienceAvailabilityId));
        Assert.Equal(0, await ReservedSlotsOfPackageAsync(catalog.PackageAvailabilityId));
        Assert.Equal(0, await QueryDbAsync(db => db.Reservations.AsNoTracking().CountAsync(r => r.AiItineraryId == itinerary.Id)));
        Assert.Equal(AiItineraryStatus.DRAFT, await QueryDbAsync(db => db.AiItineraries.AsNoTracking()
            .Where(i => i.Id == itinerary.Id).Select(i => i.Status).FirstAsync()));
    }

    [Fact]
    public async Task Book_TwoTouristsCompetingForTheLastSlots_ExactlyOneWins()
    {
        // Capacidad justa para UN itinerario de 2 viajeros en cada producto.
        var catalog = await SeedCatalogAsync("book-compete", experienceSlots: 2, packageSlots: 2);
        var (touristA, itineraryA) = await GenerateItineraryAsync(catalog, "book-compete-a");
        var (touristB, itineraryB) = await GenerateItineraryAsync(catalog, "book-compete-b");

        var responses = await Task.WhenAll(
            BookAsync(touristA, itineraryA.Id),
            BookAsync(touristB, itineraryB.Id));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        // Sin overbooking: exactamente 2 cupos tomados, no 4.
        Assert.Equal(2, await ReservedSlotsOfExperienceAsync(catalog.ExperienceAvailabilityId));
        Assert.Equal(2, await ReservedSlotsOfPackageAsync(catalog.PackageAvailabilityId));

        // Solo el ganador quedó BOOKED; el perdedor sigue reservable y sin ítems huérfanos.
        var statuses = await QueryDbAsync(db => db.AiItineraries.AsNoTracking()
            .Where(i => i.Id == itineraryA.Id || i.Id == itineraryB.Id)
            .Select(i => i.Status).ToListAsync());
        Assert.Single(statuses, s => s == AiItineraryStatus.BOOKED);
        Assert.Single(statuses, s => s != AiItineraryStatus.BOOKED);
    }

    // ---- Precio y moneda ----

    [Fact]
    public async Task Book_PriceChangedWithoutAcceptance_HoldsNothing_ThenSucceedsWhenAccepted()
    {
        var catalog = await SeedCatalogAsync("book-price");
        var (tourist, itinerary) = await GenerateItineraryAsync(catalog, "book-price-t");

        await catalog.ExperienceProvider.PutAsJsonAsync($"/api/experiences/{catalog.ExperienceId}", new
        {
            title = $"Tour Book caro {Guid.NewGuid().ToString("N")[..6]}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = catalog.CityId,
            categoryIds = Array.Empty<Guid>(),
            price = 65,
            currency = "USD"
        });

        // 1) Sin aceptar: no se toma nada y se explica qué cambió.
        var blocked = await BookAsync(tourist, itinerary.Id, acceptPriceChanges: false);
        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);
        var blockedBody = await blocked.Content.ReadFromJsonAsync<BookItineraryResponse>(JsonOptions);

        Assert.True(blockedBody!.RequiresPriceAcceptance);
        Assert.Null(blockedBody.Reservation);
        var change = Assert.Single(blockedBody.Changes);
        Assert.Equal("PRICE_CHANGED", change.ChangeType);
        Assert.Equal(40, change.PreviousUnitPrice);
        Assert.Equal(65, change.CurrentUnitPrice);
        Assert.Equal(0, await ReservedSlotsOfExperienceAsync(catalog.ExperienceAvailabilityId));

        // 2) Aceptando: se reserva al precio VIGENTE.
        var accepted = await BookAsync(tourist, itinerary.Id, acceptPriceChanges: true);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var acceptedBody = await accepted.Content.ReadFromJsonAsync<BookItineraryResponse>(JsonOptions);

        var item = Assert.Single(acceptedBody!.Reservation!.Items, i => i.ExperienceId == catalog.ExperienceId);
        Assert.Equal(65, item.UnitPrice);
        Assert.Equal(130, item.Subtotal);

        // Y el snapshot histórico de la IA sigue intacto: los dos mundos conviven.
        var aiSnapshot = await QueryDbAsync(db => db.AiItineraryItems.AsNoTracking()
            .Where(i => i.AiItineraryId == itinerary.Id && i.ExperienceId == catalog.ExperienceId)
            .Select(i => i.EstimatedUnitPrice).FirstAsync());
        Assert.Equal(40, aiSnapshot);
    }

    [Fact]
    public async Task Book_CurrencyChanged_IsReportedSeparatelyFromAPriceChange()
    {
        var catalog = await SeedCatalogAsync("book-fx");
        var (tourist, itinerary) = await GenerateItineraryAsync(catalog, "book-fx-t");

        // Mismo número, otra moneda: nunca puede tratarse como "el precio no cambió".
        await catalog.ExperienceProvider.PutAsJsonAsync($"/api/experiences/{catalog.ExperienceId}", new
        {
            title = $"Tour Book BOB {Guid.NewGuid().ToString("N")[..6]}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = catalog.CityId,
            categoryIds = Array.Empty<Guid>(),
            price = 40,
            currency = "BOB"
        });

        var body = await (await BookAsync(tourist, itinerary.Id))
            .Content.ReadFromJsonAsync<BookItineraryResponse>(JsonOptions);

        Assert.True(body!.RequiresPriceAcceptance);
        var change = Assert.Single(body.Changes, c => c.ChangeType == "CURRENCY_CHANGED");
        Assert.Equal("USD", change.PreviousCurrency);
        Assert.Equal("BOB", change.CurrentCurrency);
    }

    // ---- Pago (reutiliza UC-T-19) ----

    [Fact]
    public async Task Pay_AiReservation_UsesTheExistingFlowAndConfirmsMultiCurrency()
    {
        var catalog = await SeedCatalogAsync("book-pay");
        var (tourist, itinerary) = await GenerateItineraryAsync(catalog, "book-pay-t");

        var booked = await (await BookAsync(tourist, itinerary.Id)).Content.ReadFromJsonAsync<BookItineraryResponse>(JsonOptions);
        var reservationId = booked!.Reservation!.Id;

        // Rechazo primero: sigue reintentable, igual que una reserva directa.
        var declined = await (await tourist.PostAsJsonAsync($"/api/reservations/{reservationId}/pay", new { success = false }))
            .Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        Assert.Equal("PENDING_PAYMENT", declined!.Status);
        Assert.False(declined.PaymentApproved);

        // Y ahora aprobado, con dos monedas en la misma reserva.
        var paid = await (await tourist.PostAsJsonAsync($"/api/reservations/{reservationId}/pay", new { success = true }))
            .Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        Assert.Equal("CONFIRMED", paid!.Status);
        Assert.True(paid.PaymentApproved);
        Assert.All(paid.Items, i => Assert.Equal("CONFIRMED", i.Status));
        Assert.Equal(2, paid.Totals.Count); // los subtotales siguen separados por moneda
    }

    [Fact]
    public async Task Pay_AfterAnotherPriceChange_StillAsksForAcceptance()
    {
        var catalog = await SeedCatalogAsync("book-pay-price");
        var (tourist, itinerary) = await GenerateItineraryAsync(catalog, "book-pay-price-t");

        var booked = await (await BookAsync(tourist, itinerary.Id)).Content.ReadFromJsonAsync<BookItineraryResponse>(JsonOptions);
        var reservationId = booked!.Reservation!.Id;

        // El precio vuelve a cambiar ENTRE el booking y el pago: aplica la política de UC-SYS-02 ya existente.
        await catalog.ExperienceProvider.PutAsJsonAsync($"/api/experiences/{catalog.ExperienceId}", new
        {
            title = $"Tour Book carisimo {Guid.NewGuid().ToString("N")[..6]}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = catalog.CityId,
            categoryIds = Array.Empty<Guid>(),
            price = 90,
            currency = "USD"
        });

        var blocked = await (await tourist.PostAsJsonAsync($"/api/reservations/{reservationId}/pay", new { success = true }))
            .Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        Assert.True(blocked!.RequiresPriceAcceptance);
        Assert.Equal("PENDING_PAYMENT", blocked.Status);
        Assert.Null(blocked.PaymentApproved); // el gateway ni se invocó

        // El snapshot de la IA nunca se toca por el flujo de pago.
        var aiSnapshot = await QueryDbAsync(db => db.AiItineraryItems.AsNoTracking()
            .Where(i => i.AiItineraryId == itinerary.Id && i.ExperienceId == catalog.ExperienceId)
            .Select(i => i.EstimatedUnitPrice).FirstAsync());
        Assert.Equal(40, aiSnapshot);

        var confirmed = await (await tourist.PostAsJsonAsync($"/api/reservations/{reservationId}/pay",
            new { success = true, acceptPriceChanges = true })).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        Assert.Equal("CONFIRMED", confirmed!.Status);
    }

    // ---- Visibilidad del Provider ----

    [Fact]
    public async Task ProviderSeesOnlyItsOwnItemOfAMultiProviderAiReservation()
    {
        var catalog = await SeedCatalogAsync("book-vis");
        var (tourist, itinerary) = await GenerateItineraryAsync(catalog, "book-vis-t");

        var booked = await (await BookAsync(tourist, itinerary.Id)).Content.ReadFromJsonAsync<BookItineraryResponse>(JsonOptions);
        var experienceItemId = booked!.Reservation!.Items.Single(i => i.ExperienceId == catalog.ExperienceId).Id;
        var packageItemId = booked.Reservation.Items.Single(i => i.PackageId == catalog.PackageId).Id;

        // Cada proveedor ve su propia línea...
        Assert.Equal(HttpStatusCode.OK, (await catalog.ExperienceProvider.GetAsync($"/api/companies/me/reservations/{experienceItemId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await catalog.PackageProvider.GetAsync($"/api/companies/me/reservations/{packageItemId}")).StatusCode);

        // ...y no la del otro, aunque compartan la misma Reservation.
        Assert.Equal(HttpStatusCode.Forbidden, (await catalog.ExperienceProvider.GetAsync($"/api/companies/me/reservations/{packageItemId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await catalog.PackageProvider.GetAsync($"/api/companies/me/reservations/{experienceItemId}")).StatusCode);
    }

    // ---- UC-SYS-09: frescura del catálogo para el RAG ----

    [Fact]
    public async Task Retrieval_ReflectsCatalogChangesImmediately_NoReindexNeeded()
    {
        // UC-SYS-09 con retrieval estructurado sobre Postgres: no hay índice secundario que actualizar,
        // así que cualquier cambio commiteado es visible en la siguiente consulta de la IA.
        var catalog = await SeedCatalogAsync("rag-fresh");

        async Task<ItineraryResponse?> ProposeAsync(string prefix)
        {
            var (_, itinerary) = await GenerateItineraryAsync(catalog, prefix);
            return itinerary;
        }

        var before = await ProposeAsync("rag-fresh-1");
        Assert.Contains(before!.Items, i => i.ExperienceId == catalog.ExperienceId);

        // 1) Cambio de precio → la propuesta siguiente ya usa el precio nuevo.
        await catalog.ExperienceProvider.PutAsJsonAsync($"/api/experiences/{catalog.ExperienceId}", new
        {
            title = $"Tour RAG {Guid.NewGuid().ToString("N")[..6]}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = catalog.CityId,
            categoryIds = Array.Empty<Guid>(),
            price = 55,
            currency = "USD"
        });

        var afterPrice = await ProposeAsync("rag-fresh-2");
        Assert.Equal(55, Assert.Single(afterPrice!.Items, i => i.ExperienceId == catalog.ExperienceId).EstimatedUnitPrice);

        // 2) Despublicar → deja de ser candidata inmediatamente.
        await catalog.ExperienceProvider.PostAsync($"/api/experiences/{catalog.ExperienceId}/unpublish", null);
        var afterUnpublish = await ProposeAsync("rag-fresh-3");
        Assert.DoesNotContain(afterUnpublish!.Items, i => i.ExperienceId == catalog.ExperienceId);

        // 3) Republicar → vuelve a ser candidata.
        await catalog.ExperienceProvider.PostAsync($"/api/experiences/{catalog.ExperienceId}/publish", null);
        var afterRepublish = await ProposeAsync("rag-fresh-4");
        Assert.Contains(afterRepublish!.Items, i => i.ExperienceId == catalog.ExperienceId);

        // 4) Cerrar la disponibilidad → deja de ser candidata aunque siga publicada.
        await QueryDbAsync(async db =>
        {
            await db.ExperienceAvailabilities
                .Where(a => a.Id == catalog.ExperienceAvailabilityId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AvailabilitySlotStatus.CLOSED));
            return true;
        });

        var afterClose = await ProposeAsync("rag-fresh-5");
        Assert.DoesNotContain(afterClose!.Items, i => i.ExperienceId == catalog.ExperienceId);
    }
}
