using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Admin.Dtos;
using TurisClick.Api.Modules.Auth.Dtos;
using TurisClick.Api.Modules.Auth.Entities;
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
        var users = await (await admin.GetAsync("/api/admin/users")).Content.ReadAsStringAsync();
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

    private async Task<T> QueryDbAsync<T>(Func<TurisClickDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<TurisClickDbContext>());
    }
}
