using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Auth.Entities;
using Xunit;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// Levanta TurisClick.Api en memoria (TestServer, sin proceso/exe real) contra la base de datos
/// dedicada de tests — nunca contra turisclick_v2_dev. Aplica las migraciones
/// programáticamente (Database.MigrateAsync) en vez de invocar `dotnet ef` como proceso aparte,
/// y siembra un usuario ADMIN (no hay endpoint de auto-registro de ADMIN por diseño — ver
/// use-cases.md) para que los tests de UC-A-01/02/03/04/05 puedan autenticarse como admin real.
///
/// No hay credenciales en código: la base se toma de `ConnectionStrings:TestDatabase` (variable de
/// entorno en CI, user-secrets en local) y la contraseña del admin se genera en cada corrida.
/// Ver docs/backend-architecture.md, punto 12.2.
/// </summary>
public class TurisClickApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string TestConnectionStringKey = "ConnectionStrings:TestDatabase";

    private static readonly Lazy<string> TestConnectionString = new(ResolveTestConnectionString);

    public const string AdminEmail = "admin@turisclick.dev";

    /// <summary>
    /// Distinta en cada corrida: sólo sirve para autenticarse contra la base de tests que esta misma
    /// factory prepara, así que no hay una contraseña fija que versionar ni rotar.
    /// </summary>
    public static readonly string AdminPassword = $"test-admin-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestConnectionString.Value,
                ["Jwt:Key"] = "integration-test-signing-key-at-least-32-bytes-long-0123456789",
                ["Jwt:Issuer"] = "TurisClick.Api.Tests",
                ["Jwt:Audience"] = "TurisClick.Client.Tests",
                ["Jwt:AccessTokenExpirationMinutes"] = "15",
                ["Jwt:RefreshTokenExpirationDays"] = "30",
                // El proceso automático de expiración (UC-SYS-08) se apaga en los tests: si corriera
                // por su cuenta podría expirar una reserva en medio de otra prueba. Los tests que
                // ejercitan la expiración invocan IReservationExpirationService directamente, sin
                // depender de un timer real.
                ["Reservations:Expiration:Enabled"] = "false"
            });
        });
    }

    public async Task InitializeAsync()
    {
        // Se resuelve antes de construir el host: si falla dentro de ConfigureWebHost, el host de
        // pruebas oculta el motivo tras "The entry point exited without ever building an IHost".
        _ = TestConnectionString.Value;

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TurisClickDbContext>();
        await db.Database.MigrateAsync();

        // La base de tests persiste entre corridas y la contraseña cambia en cada una: el hash del
        // admin se reescribe siempre, no sólo al crearlo.
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasherService>();
        var admin = await db.Users.FirstOrDefaultAsync(u => u.Email == AdminEmail);
        if (admin is null)
        {
            db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                FirstName = "Admin",
                LastName = "TurisClick",
                Email = AdminEmail,
                PasswordHash = passwordHasher.Hash(AdminPassword),
                Role = UserRole.ADMIN,
                Status = UserStatus.ACTIVE,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            admin.PasswordHash = passwordHasher.Hash(AdminPassword);
        }

        await db.SaveChangesAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Task.CompletedTask;
    }

    private static string ResolveTestConnectionString()
    {
        // Variables de entorno (CI: ConnectionStrings__TestDatabase) pisan a los user-secrets locales.
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration[TestConnectionStringKey];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Falta '{TestConnectionStringKey}'. En local: dotnet user-secrets set \"{TestConnectionStringKey}\" \"...\" " +
                "desde src/TurisClick.Api. En CI: variable de entorno ConnectionStrings__TestDatabase. " +
                "Ver docs/backend-architecture.md, punto 12.2.");
        }

        // Los tests escriben datos: un connection string equivocado no debe poder apuntar a la base de dev.
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        if (string.IsNullOrWhiteSpace(database) || !database.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"'{TestConnectionStringKey}' debe apuntar a una base de tests (el nombre debe contener 'test').");
        }

        return connectionString;
    }
}
