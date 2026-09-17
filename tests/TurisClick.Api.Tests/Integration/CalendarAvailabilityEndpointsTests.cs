using System.Net;
using System.Net.Http.Json;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Modules.Packages.Dtos;
using TurisClick.Api.Modules.Reservations.Dtos;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>Calendario del proveedor: alta masiva por patrón y ajuste de fechas puntuales.</summary>
[Collection(ApiCollection.Name)]
public class CalendarAvailabilityEndpointsTests
{
    private readonly TurisClickApiFactory _factory;

    public CalendarAvailabilityEndpointsTests(TurisClickApiFactory factory) => _factory = factory;

    /// <summary>Primer lunes a partir de dentro de 7 días: el rango siempre es futuro y con días de semana conocidos.</summary>
    private static DateOnly NextMonday()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        while (date.DayOfWeek != DayOfWeek.Monday) date = date.AddDays(1);
        return date;
    }

    private async Task<(HttpClient ProviderClient, ExperienceResponse Experience, PackageResponse Package)> CreateOwnedProductsAsync(string emailPrefix)
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
            price = 30m,
            currency = "BOB"
        })).Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);

        var package = await (await providerClient.PostAsJsonAsync("/api/packages", new
        {
            title = $"Paquete-{suffix}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = city.Id,
            categoryIds = Array.Empty<Guid>(),
            durationDays = 2,
            price = 200m,
            currency = "BOB",
            items = new object[] { new { dayNumber = 1, sortOrder = 0, kind = "EXPERIENCE_REFERENCE", experienceId = experience!.Id } },
            images = Array.Empty<object>()
        })).Content.ReadFromJsonAsync<PackageResponse>(JsonOptions);

        return (providerClient, experience, package!);
    }

    private static async Task<BulkExperienceAvailabilityResponse> ReadBulk(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<BulkExperienceAvailabilityResponse>(JsonOptions))!;

    // ---------------- Experiencias ----------------

    [Fact]
    public async Task Bulk_WeekdaysPreset_CreatesOnlyMondayToFriday()
    {
        var (provider, experience, _) = await CreateOwnedProductsAsync("cal-weekdays");
        var start = NextMonday();

        var response = await provider.PostAsJsonAsync($"/api/experiences/{experience.Id}/availability/bulk", new
        {
            startDate = start,
            endDate = start.AddDays(13), // dos semanas completas
            preset = "WEEKDAYS",
            startTimes = new[] { "09:00:00" },
            totalSlots = 15
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadBulk(response);
        Assert.Equal(10, body.CreatedCount);
        Assert.Equal(0, body.SkippedCount);
        Assert.All(body.Created, slot =>
        {
            Assert.DoesNotContain(slot.Date.DayOfWeek, new[] { DayOfWeek.Saturday, DayOfWeek.Sunday });
            Assert.Equal(new TimeOnly(9, 0), slot.StartTime);
            Assert.Equal(15, slot.TotalSlots);
        });

        var owned = await provider.GetFromJsonAsync<List<ExperienceAvailabilityResponse>>($"/api/experiences/mine/{experience.Id}/availability", JsonOptions);
        Assert.Equal(10, owned!.Count);
    }

    [Fact]
    public async Task Bulk_CustomDaysAndSeveralTimes_MultipliesSlots()
    {
        var (provider, experience, _) = await CreateOwnedProductsAsync("cal-custom");
        var start = NextMonday();

        var response = await provider.PostAsJsonAsync($"/api/experiences/{experience.Id}/availability/bulk", new
        {
            startDate = start,
            endDate = start.AddDays(6),
            preset = "CUSTOM",
            weekdays = new[] { 2, 4 }, // martes y jueves
            startTimes = new[] { "15:00:00", "09:00:00" },
            totalSlots = 8
        });

        var body = await ReadBulk(response);
        Assert.Equal(4, body.CreatedCount);
        Assert.Equal(2, body.Created.Select(c => c.Date).Distinct().Count());
    }

    [Fact]
    public async Task Bulk_DryRun_DoesNotWriteAnything()
    {
        var (provider, experience, _) = await CreateOwnedProductsAsync("cal-dryrun");
        var start = NextMonday();

        var response = await provider.PostAsJsonAsync($"/api/experiences/{experience.Id}/availability/bulk", new
        {
            startDate = start, endDate = start.AddDays(6), preset = "EVERY_DAY", totalSlots = 5, dryRun = true
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(7, (await ReadBulk(response)).CreatedCount);
        var owned = await provider.GetFromJsonAsync<List<ExperienceAvailabilityResponse>>($"/api/experiences/mine/{experience.Id}/availability", JsonOptions);
        Assert.Empty(owned!);
    }

    [Fact]
    public async Task Bulk_SkipsExistingSlots_WithoutModifyingThem()
    {
        var (provider, experience, _) = await CreateOwnedProductsAsync("cal-dup");
        var start = NextMonday();
        await provider.PostAsJsonAsync($"/api/experiences/{experience.Id}/availability",
            new { date = start.AddDays(1), startTime = "09:00:00", totalSlots = 3 });

        var response = await provider.PostAsJsonAsync($"/api/experiences/{experience.Id}/availability/bulk", new
        {
            startDate = start, endDate = start.AddDays(2), preset = "EVERY_DAY", startTimes = new[] { "09:00:00" }, totalSlots = 20
        });
        var body = await ReadBulk(response);

        Assert.Equal(2, body.CreatedCount);
        var skipped = Assert.Single(body.Skipped);
        Assert.Equal(start.AddDays(1), skipped.Date);
        Assert.Equal("ALREADY_EXISTS", skipped.Reason);

        var owned = await provider.GetFromJsonAsync<List<ExperienceAvailabilityResponse>>($"/api/experiences/mine/{experience.Id}/availability", JsonOptions);
        Assert.Equal(3, owned!.Single(s => s.Date == start.AddDays(1)).TotalSlots); // intacto

        // Repetir la misma generación no duplica nada.
        var again = await ReadBulk(await provider.PostAsJsonAsync($"/api/experiences/{experience.Id}/availability/bulk", new
        {
            startDate = start, endDate = start.AddDays(2), preset = "EVERY_DAY", startTimes = new[] { "09:00:00" }, totalSlots = 20
        }));
        Assert.Equal(0, again.CreatedCount);
        Assert.Equal(3, again.SkippedCount);
    }

    [Theory]
    [InlineData(-3, 5, "EVERY_DAY")]   // arranca en el pasado
    [InlineData(10, 5, "EVERY_DAY")]   // fin antes que inicio
    [InlineData(7, 400, "EVERY_DAY")]  // más de un año
    [InlineData(7, 14, "CUSTOM")]      // custom sin días
    public async Task Bulk_InvalidRangeOrPattern_Returns400(int startOffset, int endOffset, string preset)
    {
        var (provider, experience, _) = await CreateOwnedProductsAsync("cal-invalid");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var response = await provider.PostAsJsonAsync($"/api/experiences/{experience.Id}/availability/bulk", new
        {
            startDate = today.AddDays(startOffset), endDate = today.AddDays(endOffset), preset, totalSlots = 5
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Bulk_OverTheSlotLimit_Returns400AndCreatesNothing()
    {
        var (provider, experience, _) = await CreateOwnedProductsAsync("cal-limit");
        var start = NextMonday();

        var response = await provider.PostAsJsonAsync($"/api/experiences/{experience.Id}/availability/bulk", new
        {
            startDate = start, endDate = start.AddDays(200), preset = "EVERY_DAY",
            startTimes = new[] { "08:00:00", "10:00:00", "12:00:00", "14:00:00", "16:00:00", "18:00:00" },
            totalSlots = 5
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var owned = await provider.GetFromJsonAsync<List<ExperienceAvailabilityResponse>>($"/api/experiences/mine/{experience.Id}/availability", JsonOptions);
        Assert.Empty(owned!);
    }

    [Fact]
    public async Task Bulk_AnotherProviderOrTourist_IsForbidden()
    {
        var (_, experience, package) = await CreateOwnedProductsAsync("cal-owner");
        var start = NextMonday();
        var payload = new { startDate = start, endDate = start.AddDays(6), preset = "EVERY_DAY", totalSlots = 5 };

        var attacker = _factory.CreateClient();
        UseBearerToken(attacker, (await RegisterApprovedProviderAsync(attacker, _factory.CreateClient(), "cal-attacker")).AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await attacker.PostAsJsonAsync($"/api/experiences/{experience.Id}/availability/bulk", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await attacker.PostAsJsonAsync($"/api/packages/{package.Id}/availability/bulk", payload)).StatusCode);

        var tourist = _factory.CreateClient();
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, "cal-tourist"));
        Assert.Equal(HttpStatusCode.Forbidden, (await tourist.PostAsJsonAsync($"/api/experiences/{experience.Id}/availability/bulk", payload)).StatusCode);

        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync($"/api/experiences/{experience.Id}/availability/bulk", payload)).StatusCode);
    }

    [Fact]
    public async Task Update_CapacityNeverBelowReserved_AndCloseKeepsExistingReservations()
    {
        var (provider, experience, _) = await CreateOwnedProductsAsync("cal-update");
        var start = NextMonday();
        var bulk = await ReadBulk(await provider.PostAsJsonAsync($"/api/experiences/{experience.Id}/availability/bulk", new
        {
            startDate = start, endDate = start.AddDays(1), preset = "EVERY_DAY", startTimes = new[] { "10:00:00" }, totalSlots = 10
        }));
        await provider.PostAsync($"/api/experiences/{experience.Id}/publish", null);
        var slot = bulk.Created.First();

        var tourist = _factory.CreateClient();
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, "cal-update-t"));
        var reservation = await (await tourist.PostAsJsonAsync("/api/reservations", new { experienceAvailabilityId = slot.Id, travelers = 4 }))
            .Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        // Bajar el cupo por debajo de lo reservado: 409, sin cambios.
        var tooLow = await provider.PatchAsJsonAsync($"/api/experiences/{experience.Id}/availability/{slot.Id}", new { totalSlots = 3 });
        Assert.Equal(HttpStatusCode.Conflict, tooLow.StatusCode);

        var resized = await provider.PatchAsJsonAsync($"/api/experiences/{experience.Id}/availability/{slot.Id}", new { totalSlots = 6 });
        Assert.Equal(HttpStatusCode.OK, resized.StatusCode);
        Assert.Equal(2, (await resized.Content.ReadFromJsonAsync<ExperienceAvailabilityResponse>(JsonOptions))!.AvailableSlots);

        // Cerrar: desaparece del calendario público y no admite reservas nuevas, pero la existente sigue intacta.
        var closed = await provider.PatchAsJsonAsync($"/api/experiences/{experience.Id}/availability/{slot.Id}", new { status = "CLOSED" });
        Assert.Equal("CLOSED", (await closed.Content.ReadFromJsonAsync<ExperienceAvailabilityResponse>(JsonOptions))!.Status);

        var publicSlots = await _factory.CreateClient().GetFromJsonAsync<List<ExperienceAvailabilityResponse>>($"/api/experiences/{experience.Id}/availability", JsonOptions);
        Assert.DoesNotContain(publicSlots!, s => s.Id == slot.Id);

        var newReservation = await tourist.PostAsJsonAsync("/api/reservations", new { experienceAvailabilityId = slot.Id, travelers = 1 });
        Assert.False(newReservation.IsSuccessStatusCode);

        var existing = await tourist.GetFromJsonAsync<ReservationResponse>($"/api/reservations/{reservation!.Id}", JsonOptions);
        Assert.Equal("PENDING_PAYMENT", existing!.Status);
        var owned = await provider.GetFromJsonAsync<List<ExperienceAvailabilityResponse>>($"/api/experiences/mine/{experience.Id}/availability", JsonOptions);
        Assert.Equal(4, owned!.Single(s => s.Id == slot.Id).ReservedSlots);

        // Reabrir vuelve a publicarla.
        var reopened = await provider.PatchAsJsonAsync($"/api/experiences/{experience.Id}/availability/{slot.Id}", new { status = "OPEN" });
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
    }

    [Fact]
    public async Task Update_InvalidStatus_ForeignSlotOrAnotherProvider_AreRejected()
    {
        var (provider, experience, _) = await CreateOwnedProductsAsync("cal-update-guard");
        var (otherProvider, otherExperience, _) = await CreateOwnedProductsAsync("cal-update-other");
        var start = NextMonday();
        var slot = (await ReadBulk(await provider.PostAsJsonAsync($"/api/experiences/{experience.Id}/availability/bulk", new
        {
            startDate = start, endDate = start, preset = "EVERY_DAY", totalSlots = 5
        }))).Created.Single();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await provider.PatchAsJsonAsync($"/api/experiences/{experience.Id}/availability/{slot.Id}", new { status = "MAYBE" })).StatusCode);

        // El slot existe pero no pertenece a esa experiencia.
        Assert.Equal(HttpStatusCode.NotFound,
            (await otherProvider.PatchAsJsonAsync($"/api/experiences/{otherExperience.Id}/availability/{slot.Id}", new { totalSlots = 50 })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await otherProvider.PatchAsJsonAsync($"/api/experiences/{experience.Id}/availability/{slot.Id}", new { totalSlots = 50 })).StatusCode);
    }

    // ---------------- Paquetes ----------------

    [Fact]
    public async Task PackageBulk_WeekendsPreset_SkipsExistingDeparture()
    {
        var (provider, _, package) = await CreateOwnedProductsAsync("cal-pkg");
        var start = NextMonday();
        await provider.PostAsJsonAsync($"/api/packages/{package.Id}/availability", new { departureDate = start.AddDays(5), totalSlots = 2 });

        var response = await provider.PostAsJsonAsync($"/api/packages/{package.Id}/availability/bulk", new
        {
            startDate = start, endDate = start.AddDays(13), preset = "WEEKENDS", totalSlots = 12
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BulkPackageAvailabilityResponse>(JsonOptions);
        Assert.Equal(4, body!.RequestedCount);
        Assert.Equal(3, body.CreatedCount);
        Assert.Equal(start.AddDays(5), Assert.Single(body.Skipped).DepartureDate);
        Assert.All(body.Created, d => Assert.Contains(d.DepartureDate.DayOfWeek, new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }));

        var owned = await provider.GetFromJsonAsync<List<PackageAvailabilityResponse>>($"/api/packages/mine/{package.Id}/availability", JsonOptions);
        Assert.Equal(2, owned!.Single(d => d.DepartureDate == start.AddDays(5)).TotalSlots);
    }

    [Fact]
    public async Task PackageUpdate_CloseAndCapacity()
    {
        var (provider, _, package) = await CreateOwnedProductsAsync("cal-pkg-update");
        var start = NextMonday();
        var body = await (await provider.PostAsJsonAsync($"/api/packages/{package.Id}/availability/bulk", new
        {
            startDate = start, endDate = start, preset = "EVERY_DAY", totalSlots = 5
        })).Content.ReadFromJsonAsync<BulkPackageAvailabilityResponse>(JsonOptions);
        var departure = body!.Created.Single();

        var response = await provider.PatchAsJsonAsync($"/api/packages/{package.Id}/availability/{departure.Id}", new { totalSlots = 9, status = "CLOSED" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<PackageAvailabilityResponse>(JsonOptions);
        Assert.Equal(9, updated!.TotalSlots);
        Assert.Equal("CLOSED", updated.Status);
    }
}
