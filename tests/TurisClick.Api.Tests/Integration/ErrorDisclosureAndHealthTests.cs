using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
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

    // ================================================================ /api/health

    [Fact]
    public async Task HealthInformaLaBaseRealYQueNoHayMigracionesPendientes()
    {
        var response = await _factory.CreateClient().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var health = (await response.Content.ReadFromJsonAsync<HealthResponse>(JsonOptions))!;

        string[] expected;
        string database;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TurisClickDbContext>();
            expected = db.Database.GetMigrations().ToArray();
            database = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).Database!;
        }

        Assert.Equal(HealthStatus.Healthy, health.Status);
        Assert.Equal(database, health.Database);
        Assert.Equal(0, health.PendingMigrations);
        Assert.Equal(expected.Length, health.AppliedMigrations);
        Assert.Equal(expected[^1], health.LatestMigration);
    }

    [Fact]
    public async Task HealthNoExponeDatosDeConexion()
    {
        var raw = await (await _factory.CreateClient().GetAsync("/api/health")).Content.ReadAsStringAsync();

        foreach (var fragment in new[] { "Host", "Username", "Password", "localhost", "Port" })
            Assert.DoesNotContain(fragment, raw, StringComparison.OrdinalIgnoreCase);
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

        var response = await unreachable.CreateClient().GetAsync("/api/health");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var health = JsonSerializer.Deserialize<HealthResponse>(raw, JsonOptions)!;
        Assert.Equal(HealthStatus.DatabaseUnavailable, health.Status);
        Assert.Null(health.Database);
        foreach (var fragment in new[] { "127.0.0.1", "unreachable_test", "nobody", "not-a-secret", "refused" })
            Assert.DoesNotContain(fragment, raw, StringComparison.OrdinalIgnoreCase);
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
