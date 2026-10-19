using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Admin.Dtos;
using TurisClick.Api.Modules.Admin.Services;
using TurisClick.Api.Modules.Auth.Dtos;
using TurisClick.Api.Modules.Auth.Entities;
using TurisClick.Api.Shared.Responses;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// Alta de administradores (POST /api/admin/admin-accounts). Es la operación que da el rol más alto, así que lo
/// que se protege es quién puede hacerla, que la credencial temporal sirva sólo para cambiarla, y que un email
/// existente nunca termine con su cuenta modificada.
/// </summary>
[Collection(ApiCollection.Name)]
public class AdminAccountsTests(TurisClickApiFactory factory)
{
    private const string Endpoint = "/api/admin/admin-accounts";
    private const string NewPassword = "AdministradorTurisClick2026!";

    private readonly TurisClickApiFactory _factory = factory;

    private static object Payload(string email, string firstName = "Ricardo", string lastName = "Prueba") =>
        new { firstName, lastName, email };

    private static string NewEmail() => $"admin.{Guid.NewGuid():N}@turisclick.dev";

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await LoginAsAdminAsync(client));
        return client;
    }

    private async Task<AdminAccountCreatedResponse> CreateAsync(HttpClient admin, string email)
    {
        var response = await admin.PostAsJsonAsync(Endpoint, Payload(email));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AdminAccountCreatedResponse>(JsonOptions))!;
    }

    // ================================================================ quién puede

    [Fact]
    public async Task UnAnonimoNoPuedeCrearAdministradores()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(Endpoint, Payload(NewEmail()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnTuristaNoPuedeCrearAdministradores()
    {
        var tourist = _factory.CreateClient();
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, "admin-escalada"));
        var email = NewEmail();

        var response = await tourist.PostAsJsonAsync(Endpoint, Payload(email));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(await QueryDbAsync(db => db.Users.AnyAsync(u => u.Email == email)));
    }

    [Fact]
    public async Task UnOperadorNoPuedeCrearAdministradores()
    {
        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), "admin-escalada-op");
        UseBearerToken(providerClient, provider.AccessToken);

        var response = await providerClient.PostAsJsonAsync(Endpoint, Payload(NewEmail()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ================================================================ alta

    [Fact]
    public async Task ElAdminCreaOtroAdminYRecibeLaCredencialTemporalUnaSolaVez()
    {
        var admin = await AdminClientAsync();
        var email = NewEmail();

        // El email se normaliza: es la misma cuenta se escriba como se escriba.
        var response = await admin.PostAsJsonAsync(Endpoint, Payload($"  {email.ToUpperInvariant()} "));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var account = (await response.Content.ReadFromJsonAsync<AdminAccountCreatedResponse>(JsonOptions))!;

        Assert.Equal(email, account.Email);
        Assert.Equal("Ricardo Prueba", account.FullName);
        Assert.True(account.MustChangePassword);
        Assert.True(account.TemporaryPassword.Length >= 12);

        var stored = await QueryDbAsync(db => db.Users.AsNoTracking().FirstAsync(u => u.Id == account.UserId));
        Assert.Equal(UserRole.ADMIN, stored.Role);
        Assert.Equal(UserStatus.ACTIVE, stored.Status);
        Assert.True(stored.MustChangePassword);
        Assert.Null(stored.CompanyId);
        // De la contraseña temporal sólo queda el hash.
        Assert.DoesNotContain(account.TemporaryPassword, stored.PasswordHash);

        // Y no se puede volver a consultar: el listado de usuarios no la expone.
        var users = await (await admin.GetAsync($"/api/admin/users?search={Uri.EscapeDataString(email)}"))
            .Content.ReadAsStringAsync();
        Assert.Contains(account.UserId.ToString(), users);
        Assert.DoesNotContain(account.TemporaryPassword, users);
        Assert.DoesNotContain("passwordHash", users, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CadaAltaGeneraUnaContrasenaDistinta()
    {
        var admin = await AdminClientAsync();

        var first = await CreateAsync(admin, NewEmail());
        var second = await CreateAsync(admin, NewEmail());

        Assert.NotEqual(first.TemporaryPassword, second.TemporaryPassword);
    }

    // ================================================================ emails existentes

    [Fact]
    public async Task UnEmailExistenteSeRechazaYEsaCuentaNoSeToca()
    {
        // Una cuenta de turista ya registrada: pedir un admin con su email no debe promoverla ni cambiarle nada.
        var tourist = _factory.CreateClient();
        await RegisterAndLoginTouristAsync(tourist, "ya-existe");
        var existing = await QueryDbAsync(db => db.Users.AsNoTracking()
            .Where(u => u.Email.StartsWith("ya-existe.")).OrderByDescending(u => u.CreatedAt).FirstAsync());

        var admin = await AdminClientAsync();
        var response = await admin.PostAsJsonAsync(Endpoint, Payload(existing.Email.ToUpperInvariant(), "Otro", "Nombre"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.DoesNotContain("temporaryPassword", await response.Content.ReadAsStringAsync());

        var after = await QueryDbAsync(db => db.Users.AsNoTracking().FirstAsync(u => u.Id == existing.Id));
        Assert.Equal(existing.Role, after.Role);
        Assert.Equal(existing.PasswordHash, after.PasswordHash);
        Assert.Equal(existing.MustChangePassword, after.MustChangePassword);
        Assert.Equal(existing.FirstName, after.FirstName);
        Assert.Equal(existing.LastName, after.LastName);
        Assert.Equal(existing.Status, after.Status);
        Assert.Equal(1, await QueryDbAsync(db => db.Users.CountAsync(u => u.Email == existing.Email)));
    }

    [Fact]
    public async Task CrearDosVecesElMismoAdminDevuelveConflictoLaSegunda()
    {
        var admin = await AdminClientAsync();
        var email = NewEmail();
        var first = await CreateAsync(admin, email);

        var second = await admin.PostAsJsonAsync(Endpoint, Payload(email));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        // La primera credencial sigue siendo la válida: el segundo intento no la regeneró.
        var login = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email, password = first.TemporaryPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    // ================================================================ contraseña temporal

    [Fact]
    public async Task ConLaContrasenaTemporalElNuevoAdminSoloPuedeCambiarla()
    {
        var admin = await AdminClientAsync();
        var account = await CreateAsync(admin, NewEmail());

        var client = _factory.CreateClient();
        var login = (await (await client.PostAsJsonAsync("/api/auth/login",
                new { email = account.Email, password = account.TemporaryPassword }))
            .Content.ReadFromJsonAsync<AuthResultResponse>(JsonOptions))!;

        Assert.True(login.User.MustChangePassword);
        Assert.Equal("ADMIN", login.User.Role);
        UseBearerToken(client, login.AccessToken);

        // Con la temporal no ve la plataforma ni puede crear otro administrador.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Endpoint, Payload(NewEmail()))).StatusCode);

        var changed = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = account.TemporaryPassword,
            newPassword = NewPassword,
        });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        var session = (await changed.Content.ReadFromJsonAsync<AuthResultResponse>(JsonOptions))!;
        Assert.False(session.User.MustChangePassword);
        UseBearerToken(client, session.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/overview")).StatusCode);

        // La temporal deja de servir.
        var old = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = account.Email, password = account.TemporaryPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
    }

    // ================================================================ validación

    [Theory]
    [InlineData("", "Prueba", "valido@turisclick.dev")]
    [InlineData("Ricardo", "", "valido@turisclick.dev")]
    [InlineData("Ricardo", "Prueba", "")]
    [InlineData("Ricardo", "Prueba", "no-es-un-email")]
    public async Task DatosInvalidosSeRechazanSinCrearNada(string firstName, string lastName, string email)
    {
        var admin = await AdminClientAsync();
        var before = await QueryDbAsync(db => db.Users.CountAsync());

        var response = await admin.PostAsJsonAsync(Endpoint, new { firstName, lastName, email });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await QueryDbAsync(db => db.Users.CountAsync()));
    }

    [Fact]
    public async Task NoSeAceptaUnaContrasenaElegidaPorQuienDaElAlta()
    {
        // El campo no existe: si alguien lo manda, se ignora y la contraseña la genera el servidor igual.
        var admin = await AdminClientAsync();
        var email = NewEmail();

        var response = await admin.PostAsJsonAsync(Endpoint,
            new { firstName = "Ricardo", lastName = "Prueba", email, password = "Elegida123!" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var account = (await response.Content.ReadFromJsonAsync<AdminAccountCreatedResponse>(JsonOptions))!;

        Assert.NotEqual("Elegida123!", account.TemporaryPassword);
        var chosen = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email, password = "Elegida123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, chosen.StatusCode);
    }

    // ================================================================ regenerar la credencial de otro admin

    private static string ResetEndpoint(Guid userId) => $"{Endpoint}/{userId}/reset-password";

    private async Task<AuthResultResponse> LoginAsync(string email, string password)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthResultResponse>(JsonOptions))!;
    }

    /// <summary>Un admin recién creado que ya cambió su contraseña temporal: una cuenta "en uso".</summary>
    private async Task<(AdminAccountCreatedResponse Account, AuthResultResponse Session)> ActiveAdminAsync(HttpClient admin)
    {
        var account = await CreateAsync(admin, NewEmail());
        var client = _factory.CreateClient();
        UseBearerToken(client, (await LoginAsync(account.Email, account.TemporaryPassword)).AccessToken);
        var changed = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = account.TemporaryPassword, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        return (account, (await changed.Content.ReadFromJsonAsync<AuthResultResponse>(JsonOptions))!);
    }

    [Fact]
    public async Task SoloUnAdminPuedeRegenerarLaCredencialDeOtroAdmin()
    {
        var admin = await AdminClientAsync();
        var target = await CreateAsync(admin, NewEmail());

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _factory.CreateClient().PostAsync(ResetEndpoint(target.UserId), null)).StatusCode);

        var tourist = _factory.CreateClient();
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, "reset-escalada"));
        Assert.Equal(HttpStatusCode.Forbidden, (await tourist.PostAsync(ResetEndpoint(target.UserId), null)).StatusCode);

        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), "reset-escalada-op");
        UseBearerToken(providerClient, provider.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await providerClient.PostAsync(ResetEndpoint(target.UserId), null)).StatusCode);

        // La credencial original sigue valiendo: ninguno de esos intentos la cambió.
        Assert.True((await LoginAsync(target.Email, target.TemporaryPassword)).User.MustChangePassword);
    }

    [Fact]
    public async Task UnAdminConLaContrasenaTemporalNoPuedeRegenerarOtra()
    {
        var admin = await AdminClientAsync();
        var pending = await CreateAsync(admin, NewEmail());
        var target = await CreateAsync(admin, NewEmail());

        var client = _factory.CreateClient();
        UseBearerToken(client, (await LoginAsync(pending.Email, pending.TemporaryPassword)).AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(ResetEndpoint(target.UserId), null)).StatusCode);
    }

    [Fact]
    public async Task RegenerarDaUnaContrasenaNuevaUnaSolaVezYObligaACambiarla()
    {
        var admin = await AdminClientAsync();
        var (account, session) = await ActiveAdminAsync(admin);
        var before = await QueryDbAsync(db => db.Users.AsNoTracking().FirstAsync(u => u.Id == account.UserId));
        Assert.False(before.MustChangePassword);

        var response = await admin.PostAsync(ResetEndpoint(account.UserId), null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reset = (await response.Content.ReadFromJsonAsync<ResetAdminPasswordResponse>(JsonOptions))!;

        Assert.Equal(account.UserId, reset.UserId);
        Assert.Equal(account.Email, reset.Email);
        Assert.True(reset.MustChangePassword);
        Assert.True(reset.TemporaryPassword.Length >= 12);
        Assert.NotEqual(account.TemporaryPassword, reset.TemporaryPassword);

        // Sólo cambió la contraseña: ni el rol, ni el email, ni el nombre, ni el estado.
        var after = await QueryDbAsync(db => db.Users.AsNoTracking().FirstAsync(u => u.Id == account.UserId));
        Assert.Equal(before.Role, after.Role);
        Assert.Equal(before.Email, after.Email);
        Assert.Equal(before.FirstName, after.FirstName);
        Assert.Equal(before.LastName, after.LastName);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.CompanyId, after.CompanyId);
        Assert.NotEqual(before.PasswordHash, after.PasswordHash);
        Assert.DoesNotContain(reset.TemporaryPassword, after.PasswordHash);
        Assert.True(after.MustChangePassword);

        // La contraseña que tenía deja de servir; la nueva entra, pero sólo para cambiarla.
        var old = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = account.Email, password = NewPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
        var login = await LoginAsync(account.Email, reset.TemporaryPassword);
        Assert.True(login.User.MustChangePassword);
        var client = _factory.CreateClient();
        UseBearerToken(client, login.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/overview")).StatusCode);

        // Las sesiones abiertas con la credencial anterior ya no se renuevan.
        var refresh = await _factory.CreateClient().PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        // Y no hay forma de volver a consultarla.
        var users = await (await admin.GetAsync($"/api/admin/users?role=ADMIN&search={Uri.EscapeDataString(account.Email)}"))
            .Content.ReadAsStringAsync();
        Assert.Contains(account.UserId.ToString(), users);
        Assert.DoesNotContain(reset.TemporaryPassword, users);
    }

    [Fact]
    public async Task ElListadoDeAdministradoresMarcaQuienTieneLaContrasenaTemporal()
    {
        var admin = await AdminClientAsync();
        var pending = await CreateAsync(admin, NewEmail());
        var (active, _) = await ActiveAdminAsync(admin);

        // Por email: la base de tests persiste entre corridas y acumula administradores de otras pruebas.
        async Task<AdminUserResponse> FindAsync(string email)
        {
            var page = (await (await admin.GetAsync($"/api/admin/users?role=ADMIN&search={Uri.EscapeDataString(email)}"))
                .Content.ReadFromJsonAsync<PagedResult<AdminUserResponse>>(JsonOptions))!;
            Assert.All(page.Items, u => Assert.Equal("ADMIN", u.Role));
            return page.Items.Single(u => u.Email == email);
        }

        Assert.True((await FindAsync(pending.Email)).MustChangePassword);
        Assert.False((await FindAsync(active.Email)).MustChangePassword);
    }

    [Fact]
    public async Task LaPropiaContrasenaNoSeRegeneraPorAca()
    {
        var admin = await AdminClientAsync();
        var self = await QueryDbAsync(db => db.Users.AsNoTracking().FirstAsync(u => u.Email == TurisClickApiFactory.AdminEmail));

        var response = await admin.PostAsync(ResetEndpoint(self.Id), null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = await QueryDbAsync(db => db.Users.AsNoTracking().FirstAsync(u => u.Id == self.Id));
        Assert.Equal(self.PasswordHash, after.PasswordHash);
        Assert.Equal(self.MustChangePassword, after.MustChangePassword);
    }

    [Fact]
    public async Task NoSirveParaCuentasQueNoSonDeAdministrador()
    {
        var admin = await AdminClientAsync();
        var providerClient = _factory.CreateClient();
        var created = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), "reset-no-admin");
        var provider = await QueryDbAsync(db => db.Users.AsNoTracking().FirstAsync(u => u.Email == created.Email));

        var response = await admin.PostAsync(ResetEndpoint(provider.Id), null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = await QueryDbAsync(db => db.Users.AsNoTracking().FirstAsync(u => u.Id == provider.Id));
        Assert.Equal(provider.PasswordHash, after.PasswordHash);
        Assert.Equal(provider.MustChangePassword, after.MustChangePassword);
        Assert.Equal(UserRole.PROVIDER, after.Role);
    }

    [Fact]
    public async Task UnaCuentaInexistenteDevuelve404()
    {
        var admin = await AdminClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync(ResetEndpoint(Guid.NewGuid()), null)).StatusCode);
    }

    [Fact]
    public async Task ElLogDejaConstanciaDeQuienYAQuienPeroNuncaLaContrasena()
    {
        // Serilog toma el logging del host, así que el servicio se arma a mano con un logger que captura todo,
        // sobre la misma base de tests.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TurisClickDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasherService>();
        var actorId = (await db.Users.AsNoTracking().FirstAsync(u => u.Email == TurisClickApiFactory.AdminEmail)).Id;
        var logger = new CapturingLogger();
        var service = new AdminOnboardingService(db, hasher, new FixedCurrentUser(actorId), logger);

        var created = await service.CreateAdminAccountAsync(
            new CreateAdminAccountRequest { FirstName = "Ricardo", LastName = "Log", Email = NewEmail() }, CancellationToken.None);
        var reset = await service.ResetAdminPasswordAsync(created.UserId, CancellationToken.None);

        var text = string.Join('\n', logger.Messages);
        Assert.Equal(2, logger.Messages.Count);
        Assert.Contains(actorId.ToString(), text);
        Assert.Contains(created.UserId.ToString(), text);
        Assert.DoesNotContain(created.TemporaryPassword, text);
        Assert.DoesNotContain(reset.TemporaryPassword, text);
    }

    private sealed class FixedCurrentUser(Guid userId) : ICurrentUserContext
    {
        public bool IsAuthenticated => true;
        public Guid UserId => userId;
        public string Role => "ADMIN";
        public Guid? CompanyId => null;
    }

    private sealed class CapturingLogger : ILogger<AdminOnboardingService>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private async Task<T> QueryDbAsync<T>(Func<TurisClickDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<TurisClickDbContext>());
    }
}
