using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Health;
using TurisClick.Api.Modules.Categories.Dtos;
using TurisClick.Api.Modules.Categories.Services;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// El pipeline real (middleware de excepciones, serialización, controllers) frente a los dos problemas
/// del incidente de octubre de 2026: que un 500 publicaba el error crudo de PostgreSQL, y que la única
/// forma de saber si la base estaba al día era justamente leer ese error.
/// </summary>
[Collection(ApiCollection.Name)]
public class ErrorDisclosureAndHealthTests
{
    private readonly TurisClickApiFactory _factory;

    public ErrorDisclosureAndHealthTests(TurisClickApiFactory factory) => _factory = factory;

    // ================================================================ errores inesperados

    [Fact]
    public async Task UnaColumnaFaltanteEnLaBaseResponde500SinElErrorDePostgres()
    {
        // Lo mismo que pasó en Azure: el código espera una columna que la base no tiene.
        using var broken = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddScoped<ICategoryService>(_ => new ThrowingCategoryService(
                new PostgresException("column u.must_change_password does not exist", "ERROR", "ERROR", "42703")))));

        var response = await broken.CreateClient().GetAsync("/api/categories");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        foreach (var fragment in new[] { "42703", "must_change_password", "does not exist", "Npgsql", "Postgres", " at " })
            Assert.DoesNotContain(fragment, raw, StringComparison.OrdinalIgnoreCase);

        using var body = JsonDocument.Parse(raw);
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task ElLoginConCredencialesInvalidasSigueExplicandoQueFallo()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = "nadie@invalid.local", password = "NoEsUnaCredencial1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Email o contraseña inválidos.", body.RootElement.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }

    // ================================================================ /api/health (público, mínimo)

    [Fact]
    public async Task HealthPublicoSoloDiceSiLaAppEstaSana()
    {
        var response = await _factory.CreateClient().GetAsync("/api/health");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(raw);
        // Una sola propiedad: ni el nombre de la base, ni las migraciones, ni datos de conexión.
        Assert.Equal(["status"], body.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(HealthStatus.Healthy, body.RootElement.GetProperty("status").GetString());

        var (database, migrations) = ExpectedSchema();
        foreach (var fragment in new[] { database, migrations[^1], "Oleada", "Host", "Username", "Password", "localhost" })
            Assert.DoesNotContain(fragment, raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LaHuellaCorrectaConfirmaBaseYMigracionesSinNombrarlas()
    {
        var (database, migrations) = ExpectedSchema();

        var health = await HealthAsync(_factory, DatabaseSchemaInspector.Fingerprint(database, migrations));

        Assert.True(health.SchemaMatches);
    }

    [Fact]
    public async Task LaHuellaDeLaBaseV1NoCoincide()
    {
        var (_, migrations) = ExpectedSchema();

        var health = await HealthAsync(_factory, DatabaseSchemaInspector.Fingerprint("turisclick_db", migrations));

        Assert.False(health.SchemaMatches);
    }

    [Fact]
    public async Task UnaHuellaConMigracionesDeMasODeMenosNoCoincide()
    {
        var (database, migrations) = ExpectedSchema();

        Assert.False((await HealthAsync(_factory, DatabaseSchemaInspector.Fingerprint(database, migrations[..^1]))).SchemaMatches);
        Assert.False((await HealthAsync(_factory, DatabaseSchemaInspector.Fingerprint(database,
            migrations.Append("20991231000000_9999_Futura")))).SchemaMatches);
    }

    [Fact]
    public async Task ConectadaAOtraBaseQueLaEsperadaLaAppNoEstaSana()
    {
        // Lo que hace appsettings.Production.json en Azure: si la app llegara a otra base, deja de estar sana.
        using var wrongDatabase = _factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseSchemaInspector.ExpectedDatabaseKey] = "turisclick_db_v2"
            })));

        var response = await wrongDatabase.CreateClient().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var health = (await response.Content.ReadFromJsonAsync<PublicHealthResponse>(JsonOptions))!;
        Assert.Equal(HealthStatus.Unhealthy, health.Status);
    }

    [Fact]
    public async Task HealthConLaBaseInalcanzableResponde503SinDecirPorQue()
    {
        // Un puerto donde no escucha nadie: la conexión falla rápido y de forma determinística.
        using var unreachable = _factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=127.0.0.1;Port=1;Database=unreachable_test;Username=nobody;Password=not-a-secret;Timeout=3"
            })));

        var (database, migrations) = ExpectedSchema();
        var response = await unreachable.CreateClient().GetAsync(
            $"/api/health?expect={DatabaseSchemaInspector.Fingerprint(database, migrations)}");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var health = JsonSerializer.Deserialize<PublicHealthResponse>(raw, JsonOptions)!;
        Assert.Equal(HealthStatus.Unhealthy, health.Status);
        Assert.False(health.SchemaMatches);
        foreach (var fragment in new[] { "127.0.0.1", "unreachable_test", "nobody", "not-a-secret", "refused" })
            Assert.DoesNotContain(fragment, raw, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ /api/admin/health/schema (ADMIN)

    [Fact]
    public async Task ElDetalleDelEsquemaExigeAutenticacion()
    {
        var response = await _factory.CreateClient().GetAsync("/api/admin/health/schema");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ElDetalleDelEsquemaNoEsParaUnTurista()
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await RegisterAndLoginTouristAsync(client, "health-tourist"));

        var response = await client.GetAsync("/api/admin/health/schema");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ElAdminVeLaBaseRealYSusMigraciones()
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await LoginAsAdminAsync(client));

        var response = await client.GetAsync("/api/admin/health/schema");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var health = (await response.Content.ReadFromJsonAsync<SchemaHealthResponse>(JsonOptions))!;
        var (database, migrations) = ExpectedSchema();
        Assert.Equal(HealthStatus.Healthy, health.Status);
        Assert.Equal(database, health.Database);
        Assert.Equal(migrations.Length, health.AppliedMigrations);
        Assert.Equal(migrations[^1], health.LatestMigration);
        Assert.Equal(0, health.PendingMigrations);
        Assert.Equal(DatabaseSchemaInspector.Fingerprint(database, migrations), health.Fingerprint);
    }

    private static async Task<PublicHealthResponse> HealthAsync(WebApplicationFactory<Program> factory, string fingerprint)
    {
        var response = await factory.CreateClient().GetAsync($"/api/health?expect={fingerprint}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PublicHealthResponse>(JsonOptions))!;
    }

    /// <summary>La base de tests y las migraciones que declara el código, que es lo que el pipeline compara.</summary>
    private (string Database, string[] Migrations) ExpectedSchema()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TurisClickDbContext>();
        return (new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).Database!,
            db.Database.GetMigrations().ToArray());
    }

    private sealed class ThrowingCategoryService(Exception exception) : ICategoryService
    {
        public Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, CancellationToken ct) => throw exception;
        public Task<CategoryResponse> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken ct) => throw exception;
        public Task DeleteAsync(Guid id, CancellationToken ct) => throw exception;
        public Task<CategoryResponse> GetByIdAsync(Guid id, CancellationToken ct) => throw exception;
        public Task<List<CategoryResponse>> ListAsync(CancellationToken ct) => throw exception;
    }
}
