using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Auth.Entities;
using Xunit;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// Levanta TurisClick.Api en memoria (TestServer, sin proceso/exe real) contra la base de datos
/// dedicada `turisclick_v2_test` — nunca contra turisclick_v2_dev. Aplica las migraciones
/// programáticamente (Database.MigrateAsync) en vez de invocar `dotnet ef` como proceso aparte,
/// y siembra un usuario ADMIN fijo (no hay endpoint de auto-registro de ADMIN por diseño — ver
/// use-cases.md) para que los tests de UC-A-01/02/03/04/05 puedan autenticarse como admin real.
/// </summary>
public class TurisClickApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string TestConnectionString =
        "Host=localhost;Port=5432;Database=turisclick_v2_test;Username=postgres;Password=hwcrhh330";

    public const string AdminEmail = "admin@turisclick.dev";
    public const string AdminPassword = "AdminPassword123!";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestConnectionString,
                ["Jwt:Key"] = "integration-test-signing-key-at-least-32-bytes-long-0123456789",
                ["Jwt:Issuer"] = "TurisClick.Api.Tests",
                ["Jwt:Audience"] = "TurisClick.Client.Tests",
                ["Jwt:AccessTokenExpirationMinutes"] = "15",
                ["Jwt:RefreshTokenExpirationDays"] = "30"
            });
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TurisClickDbContext>();
        await db.Database.MigrateAsync();

        if (!await db.Users.AnyAsync(u => u.Email == AdminEmail))
        {
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasherService>();
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
            await db.SaveChangesAsync();
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Task.CompletedTask;
    }
}
