using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Admin.Dtos;
using TurisClick.Api.Modules.Auth.Dtos;
using TurisClick.Api.Modules.Auth.Entities;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Modules.Packages.Dtos;
using TurisClick.Api.Shared.Responses;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// Alta de operadores por parte de un administrador y visibilidad global de la plataforma.
///
/// Lo que se protege acá es la puerta de entrada: que **nadie pueda darse de alta como operador**, que una
/// contraseña temporal sirva para entrar y cambiarla y para nada más, que un operador siga viendo sólo lo
/// suyo, y que el administrador vea todo sin que eso signifique exponer datos de pasajeros.
/// </summary>
[Collection(ApiCollection.Name)]
public class AdminPlatformTests(TurisClickApiFactory factory)
{
    private readonly TurisClickApiFactory _factory = factory;

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await LoginAsAdminAsync(client));
        return client;
    }

    private static object AccountPayload(string suffix, bool approve = true) => new
    {
        companyName = $"Operador {suffix}",
        companyDescription = "Operador turístico de prueba.",
        legalDocument = $"NIT-{suffix}",
        contactEmail = $"contacto.{suffix}@turisclick.dev",
        firstName = "Camila",
        lastName = "Aramayo",
        email = $"operador.{suffix}@turisclick.dev",
        approve,
    };

    // ================================================================ no hay autorregistro

    [Fact]
    public async Task ElAutorregistroPublicoDeOperadoresYaNoExiste()
    {
        // Era una escalada de privilegios servida: un endpoint anónimo que creaba cuentas con permiso de
        // publicar en el catálogo.
        var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/providers/register", new
        {
            firstName = "Alguien",
            lastName = "DeAfuera",
            email = $"intruso.{Guid.NewGuid():N}@turisclick.dev",
            password = "Password123!",
            companyName = "Empresa Fantasma",
            legalDocument = $"NIT-{Guid.NewGuid():N}",
            contactEmail = "contacto@fantasma.dev",
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnTuristaNoPuedeDarseDeAltaComoOperador()
    {
        var tourist = _factory.CreateClient();
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, "escalada"));

        var response = await tourist.PostAsJsonAsync(
            "/api/admin/provider-accounts", AccountPayload(Guid.NewGuid().ToString("N")[..8]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnOperadorNoPuedeDarDeAltaOtrosOperadores()
    {
        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), "no-alta");
        UseBearerToken(providerClient, provider.AccessToken);

        var response = await providerClient.PostAsJsonAsync(
            "/api/admin/provider-accounts", AccountPayload(Guid.NewGuid().ToString("N")[..8]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ================================================================ alta por admin

    [Fact]
    public async Task ElAdminDaDeAltaUnOperadorYRecibeSuCredencialTemporalUnaSolaVez()
    {
        var admin = await AdminClientAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var response = await admin.PostAsJsonAsync("/api/admin/provider-accounts", AccountPayload(suffix));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var account = await response.Content.ReadFromJsonAsync<ProviderAccountCreatedResponse>(JsonOptions);

        Assert.Equal("APPROVED", account!.Company.Status);
        Assert.True(account.MustChangePassword);
        Assert.False(string.IsNullOrWhiteSpace(account.TemporaryPassword));

        // La contraseña temporal NO se guarda en claro: de ella sólo queda el hash.
        var stored = await QueryDbAsync(db => db.Users.AsNoTracking().FirstAsync(u => u.Id == account.UserId));
        Assert.DoesNotContain(account.TemporaryPassword, stored.PasswordHash);
        Assert.True(stored.MustChangePassword);
        Assert.Equal(UserRole.PROVIDER, stored.Role);
        Assert.Equal(account.Company.Id, stored.CompanyId);

        // Y no hay forma de volver a consultarla: el detalle de las cuentas no la expone.
        var users = await (await admin.GetAsync($"/api/admin/companies/{account.Company.Id}/users")).Content
            .ReadAsStringAsync();
        Assert.DoesNotContain(account.TemporaryPassword, users);
        Assert.DoesNotContain("passwordHash", users);
    }

    [Fact]
    public async Task UnEmailOUnDocumentoRepetidoSeRechaza()
    {
        var admin = await AdminClientAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Assert.Equal(
            HttpStatusCode.Created,
            (await admin.PostAsJsonAsync("/api/admin/provider-accounts", AccountPayload(suffix))).StatusCode);

        // Mismo email, documento nuevo.
        var duplicateEmail = await admin.PostAsJsonAsync("/api/admin/provider-accounts", new
        {
            companyName = "Otra Empresa",
            legalDocument = $"NIT-{Guid.NewGuid():N}",
            contactEmail = "otro@turisclick.dev",
            firstName = "Otro",
            lastName = "Operador",
            email = $"operador.{suffix}@turisclick.dev",
            approve = true,
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicateEmail.StatusCode);

        // Mismo documento, email nuevo.
        var duplicateDocument = await admin.PostAsJsonAsync("/api/admin/provider-accounts", new
        {
            companyName = "Otra Empresa",
            legalDocument = $"NIT-{suffix}",
            contactEmail = "otro@turisclick.dev",
            firstName = "Otro",
            lastName = "Operador",
            email = $"nuevo.{Guid.NewGuid():N}@turisclick.dev",
            approve = true,
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicateDocument.StatusCode);
    }

    // ================================================================ contraseña temporal

    [Fact]
    public async Task ConLaContrasenaTemporalNoSePuedeOperar()
    {
        // La puerta está en la autorización, no en la pantalla: una credencial temporal filtrada —que es la
        // que circula por fuera del sistema— no sirve para publicar nada.
        var admin = await AdminClientAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var account = await (await admin.PostAsJsonAsync("/api/admin/provider-accounts", AccountPayload(suffix)))
            .Content.ReadFromJsonAsync<ProviderAccountCreatedResponse>(JsonOptions);

        var client = _factory.CreateClient();
        var login = await (await client.PostAsJsonAsync("/api/auth/login",
            new { email = account!.Email, password = account.TemporaryPassword }))
            .Content.ReadFromJsonAsync<AuthResultResponse>(JsonOptions);

        Assert.True(login!.User.MustChangePassword);
        UseBearerToken(client, login.AccessToken);

        // Ni leer lo propio ni publicar: todo lo que exige una política está cerrado.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/experiences/mine")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/companies/me")).StatusCode);

        // Lo único abierto es cambiarla.
        var changed = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = account.TemporaryPassword,
            newPassword = "OperadorTurisClick2026!",
        });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        var session = await changed.Content.ReadFromJsonAsync<AuthResultResponse>(JsonOptions);
        Assert.False(session!.User.MustChangePassword);

        UseBearerToken(client, session.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/experiences/mine")).StatusCode);
    }

    [Fact]
    public async Task CambiarLaContrasenaExigeLaActual()
    {
        var admin = await AdminClientAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var account = await (await admin.PostAsJsonAsync("/api/admin/provider-accounts", AccountPayload(suffix)))
            .Content.ReadFromJsonAsync<ProviderAccountCreatedResponse>(JsonOptions);

        var client = _factory.CreateClient();
        var login = await (await client.PostAsJsonAsync("/api/auth/login",
            new { email = account!.Email, password = account.TemporaryPassword }))
            .Content.ReadFromJsonAsync<AuthResultResponse>(JsonOptions);
        UseBearerToken(client, login!.AccessToken);

        // Sin la actual, un token robado alcanzaría para quedarse con la cuenta.
        var wrong = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = "no-es-la-mia",
            newPassword = "OperadorTurisClick2026!",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        // Y la nueva tiene que ser distinta.
        var same = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = account.TemporaryPassword,
            newPassword = account.TemporaryPassword,
        });
        Assert.Equal(HttpStatusCode.BadRequest, same.StatusCode);
    }

    [Fact]
    public async Task RegenerarLaCredencialVuelveABloquearLaCuenta()
    {
        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), "reset");
        UseBearerToken(providerClient, provider.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await providerClient.GetAsync("/api/experiences/mine")).StatusCode);

        var userId = await QueryDbAsync(db => db.Users
            .AsNoTracking().Where(u => u.Email == provider.Email).Select(u => u.Id).FirstAsync());

        var admin = await AdminClientAsync();
        var reset = await admin.PostAsync($"/api/admin/provider-accounts/{userId}/reset-password", null);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var regenerated = await reset.Content.ReadFromJsonAsync<ResetProviderPasswordResponse>(JsonOptions);
        Assert.False(string.IsNullOrWhiteSpace(regenerated!.TemporaryPassword));

        // La contraseña vieja ya no entra, y la nueva vuelve a nacer bloqueada.
        var oldPassword = await providerClient.PostAsJsonAsync("/api/auth/login",
            new { email = provider.Email, password = provider.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);

        var fresh = await (await providerClient.PostAsJsonAsync("/api/auth/login",
            new { email = provider.Email, password = regenerated.TemporaryPassword }))
            .Content.ReadFromJsonAsync<AuthResultResponse>(JsonOptions);
        Assert.True(fresh!.User.MustChangePassword);
    }

    // ================================================================ visibilidad global

    [Fact]
    public async Task ElAdminVeTodoElCatalogoYElOperadorSoloElSuyo()
    {
        var (providerClient, experienceId, packageId) = await SeedCatalogAsync("admin-catalogo");

        var admin = await AdminClientAsync();

        var experiences = await (await admin.GetAsync("/api/admin/experiences?pageSize=100")).Content
            .ReadFromJsonAsync<PagedResult<AdminExperienceRowResponse>>(JsonOptions);
        Assert.Contains(experiences!.Items, e => e.Id == experienceId);
        Assert.All(experiences.Items, e => Assert.False(string.IsNullOrWhiteSpace(e.CompanyName)));

        var packages = await (await admin.GetAsync("/api/admin/packages?pageSize=100")).Content
            .ReadFromJsonAsync<PagedResult<AdminPackageRowResponse>>(JsonOptions);
        var row = Assert.Single(packages!.Items, p => p.Id == packageId);
        Assert.True(row.IncludesFlight);
        Assert.Equal("VVI → LPB", row.FlightRoute);
        Assert.True(row.HasCancellationPolicy);
        Assert.Equal(1, row.Categories);

        // El borrador aparece para el admin: la moderación necesita ver lo no publicado.
        Assert.Contains(experiences.Items, e => e.Status == "DRAFT" || e.Status == "PUBLISHED");

        // Un operador no entra a estos listados.
        Assert.Equal(HttpStatusCode.Forbidden, (await providerClient.GetAsync("/api/admin/experiences")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await providerClient.GetAsync("/api/admin/packages")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await providerClient.GetAsync("/api/admin/reservations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await providerClient.GetAsync("/api/admin/overview")).StatusCode);
    }

    [Fact]
    public async Task ElResumenDePlataformaCuentaLoQueLaBaseSabeContar()
    {
        await SeedCatalogAsync("admin-resumen");

        var admin = await AdminClientAsync();
        var overview = await (await admin.GetAsync("/api/admin/overview")).Content
            .ReadFromJsonAsync<AdminOverviewResponse>(JsonOptions);

        Assert.True(overview!.CompaniesApproved >= 1);
        Assert.True(overview.PackagesWithFlight >= 1);
        Assert.True(overview.ExperiencesPublished + overview.ExperiencesDraft >= 1);

        // Los contadores de lo que pide acción existen y son números reales, no placeholders.
        Assert.True(overview.CancellationsNeedingReview >= 0);
        Assert.True(overview.FlightsAwaitingReconciliation >= 0);
        Assert.True(overview.ProviderAccountsPendingFirstLogin >= 0);
    }

    [Fact]
    public async Task ElAdminVeLasReservasSinDatosDePasajeros()
    {
        var (_, _, packageId) = await SeedCatalogAsync("admin-reservas");

        var availabilityId = await QueryDbAsync(db => db.PackageAvailabilities
            .AsNoTracking().Where(a => a.PackageId == packageId).Select(a => a.Id).FirstAsync());

        var tourist = _factory.CreateClient();
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, "admin-reservas-t"));

        var quote = await (await tourist.PostAsJsonAsync($"/api/packages/{packageId}/flight-quotes",
            new { originIata = "VVI", packageAvailabilityId = availabilityId, travelers = 1 }))
            .Content.ReadFromJsonAsync<TurisClick.Api.Modules.Flights.Dtos.PackageFlightQuoteResponse>(JsonOptions);

        var reservation = await (await tourist.PostAsJsonAsync("/api/reservations", new
        {
            packageAvailabilityId = availabilityId,
            travelers = 1,
            flightQuoteId = quote!.Options[0].QuoteId,
        })).Content.ReadFromJsonAsync<TurisClick.Api.Modules.Reservations.Dtos.ReservationResponse>(JsonOptions);

        await tourist.PostAsJsonAsync($"/api/reservations/{reservation!.Id}/pay", new
        {
            success = true,
            acceptPriceChanges = true,
            travelers = new[]
            {
                new
                {
                    givenName = "Zoraida",
                    familyName = "Mamani",
                    bornOn = "1990-05-14",
                    gender = "f",
                    title = "ms",
                    email = "zoraida@example.test",
                    phoneNumber = "+59170000000",
                },
            },
        });

        var admin = await AdminClientAsync();
        var raw = await (await admin.GetAsync("/api/admin/reservations?pageSize=100")).Content.ReadAsStringAsync();
        var page = await (await admin.GetAsync("/api/admin/reservations?pageSize=100")).Content
            .ReadFromJsonAsync<PagedResult<AdminReservationRowResponse>>(JsonOptions);

        var row = Assert.Single(page!.Items, r => r.Id == reservation.Id);
        Assert.Equal("CONFIRMED", row.Status);
        Assert.Equal("PACKAGE", row.Kind);
        Assert.Equal("CONFIRMED", row.FlightStatus);
        Assert.Equal("VVI → LPB", row.FlightRoute);
        Assert.NotEmpty(row.Companies);
        Assert.NotEmpty(row.Charged);

        // El pasajero del vuelo no existe en la base y no aparece acá por tener un rol global.
        Assert.DoesNotContain("Zoraida", raw);
        Assert.DoesNotContain("example.test", raw);
        Assert.DoesNotContain("ord_fake", raw);
    }

    [Fact]
    public async Task ElFiltroDeAtencionDejaSoloLoQueEsperaAAlguien()
    {
        var admin = await AdminClientAsync();

        var attention = await (await admin.GetAsync("/api/admin/reservations?needsAttention=true&pageSize=100")).Content
            .ReadFromJsonAsync<PagedResult<AdminReservationRowResponse>>(JsonOptions);

        // Todo lo que entra por este filtro tiene un pasaje sin resolver o una cancelación a medias.
        Assert.All(attention!.Items, row => Assert.True(
            row.FlightStatus is "RECONCILIATION_REQUIRED" or "ORDERING"
            || row.CancellationStatus is "REFUND_PENDING" or "REQUIRES_REVIEW" or "ACCEPTED"));
    }

    // ================================================================ auxiliares

    private async Task<T> QueryDbAsync<T>(Func<TurisClickDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<TurisClickDbContext>());
    }

    /// <summary>Un operador con una experiencia publicada y un paquete con vuelo, política y categoría.</summary>
    private async Task<(HttpClient Provider, Guid ExperienceId, Guid PackageId)> SeedCatalogAsync(string prefix)
    {
        var admin = await AdminClientAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var country = await (await admin.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"PaisAd-{suffix}", type = "COUNTRY" })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var region = await (await admin.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"RegionAd-{suffix}", type = "REGION", parentId = country!.Id })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var city = await (await admin.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"CiudadAd-{suffix}", type = "CITY", parentId = region!.Id })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);

        var category = await (await admin.PostAsJsonAsync("/api/admin/categories",
            new { name = $"Gastronomía {suffix}" })).Content.ReadFromJsonAsync<TurisClick.Api.Modules.Categories.Dtos.CategoryResponse>(JsonOptions);

        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), prefix);
        UseBearerToken(providerClient, provider.AccessToken);

        var experience = await (await providerClient.PostAsJsonAsync("/api/experiences", new
        {
            title = $"Tour gastronómico {suffix}",
            description = "Recorrido por mercados y cocinas tradicionales con guía local.",
            destinationId = city!.Id,
            categoryIds = new[] { category!.Id },
            price = 35m,
            currency = "USD",
        })).Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);

        var package = await (await providerClient.PostAsJsonAsync("/api/packages", new
        {
            title = $"Paquete con vuelo {suffix}",
            description = "Paquete de prueba con guía local, traslados y alojamiento incluidos.",
            destinationId = city.Id,
            categoryIds = new[] { category.Id },
            durationDays = 3,
            price = 500m,
            currency = "USD",
            items = new[] { new { dayNumber = 1, sortOrder = 1, kind = "DESCRIPTIVE", title = "Llegada" } },
            images = Array.Empty<object>(),
            cancellationPolicy = new[] { new { minDaysBefore = 30, refundPercentage = 100 }, new { minDaysBefore = 0, refundPercentage = 0 } },
        })).Content.ReadFromJsonAsync<PackageResponse>(JsonOptions);

        await providerClient.PostAsJsonAsync($"/api/packages/{package!.Id}/availability", new
        {
            departureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(45).ToString("yyyy-MM-dd"),
            totalSlots = 10,
        });

        await providerClient.PostAsync($"/api/packages/{package.Id}/publish", null);

        await providerClient.PutAsJsonAsync($"/api/packages/{package.Id}/flight-rule", new
        {
            destinationIata = "LPB",
            allowedOriginIatas = new[] { "VVI" },
            cabinClass = "ECONOMY",
            outboundOffsetDays = 0,
            inboundOffsetDays = 0,
            roundTrip = true,
        });

        return (providerClient, experience!.Id, package.Id);
    }
}
