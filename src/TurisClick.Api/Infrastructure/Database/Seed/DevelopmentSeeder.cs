using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Auth.Entities;
using TurisClick.Api.Modules.Categories.Entities;
using TurisClick.Api.Modules.Destinations.Entities;

namespace TurisClick.Api.Infrastructure.Database.Seed;

/// <summary>
/// Seed idempotente de DEVELOPMENT: admin de prueba, categorías base y la jerarquía real de destinos de
/// Bolivia (País → Departamento → Ciudad) desde bolivia-cities.json. Se invoca desde Program.cs solo si
/// <c>IsDevelopment() &amp;&amp; Seed:Enabled=true</c> — nunca corre en Production, y la contraseña del admin
/// se lee de configuración (user-secrets), nunca hardcodeada en el código.
/// </summary>
public static class DevelopmentSeeder
{
    public const string AdminEmail = "admin@turisclick.dev";

    /// <summary>Categorías base razonables para un catálogo de turismo — idempotente por nombre.</summary>
    private static readonly string[] BaseCategoryNames =
    [
        "Aventura", "Naturaleza", "Cultura", "Gastronomía", "Historia", "Relax y bienestar",
    ];

    public static async Task SeedAsync(IServiceProvider rootServices, CancellationToken ct = default)
    {
        using var scope = rootServices.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<TurisClickDbContext>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DevelopmentSeeder));

        await SeedAdminAsync(db, services.GetRequiredService<IPasswordHasherService>(), services.GetRequiredService<IConfiguration>(), logger, ct);
        await SeedCategoriesAsync(db, ct);
        await SeedBoliviaDestinationsAsync(db, logger, ct);

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedAdminAsync(
        TurisClickDbContext db, IPasswordHasherService passwordHasher, IConfiguration configuration, ILogger logger, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.Email == AdminEmail, ct))
            return;

        // Nunca en código: se configura una sola vez con
        // `dotnet user-secrets set "Seed:AdminPassword" "..."` (ver docs/backend-architecture.md).
        var password = configuration["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Seed:AdminPassword no está configurado (dotnet user-secrets) — se omite la creación del admin de prueba.");
            return;
        }

        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            FirstName = "Admin",
            LastName = "TurisClick",
            Email = AdminEmail,
            PasswordHash = passwordHasher.Hash(password),
            Role = UserRole.ADMIN,
            Status = UserStatus.ACTIVE,
        });

        logger.LogInformation("Seed: admin de prueba creado ({Email}).", AdminEmail);
    }

    private static async Task SeedCategoriesAsync(TurisClickDbContext db, CancellationToken ct)
    {
        var existingNames = new HashSet<string>(
            await db.Categories.Select(c => c.Name).ToListAsync(ct), StringComparer.OrdinalIgnoreCase);

        foreach (var name in BaseCategoryNames)
        {
            if (existingNames.Contains(name)) continue;
            db.Categories.Add(new Category { Id = Guid.NewGuid(), Name = name });
        }
    }

    private static async Task SeedBoliviaDestinationsAsync(TurisClickDbContext db, ILogger logger, CancellationToken ct)
    {
        var jsonPath = Path.Combine(AppContext.BaseDirectory, "Infrastructure", "Database", "Seed", "bolivia-cities.json");
        if (!File.Exists(jsonPath))
        {
            logger.LogWarning("Seed: no se encontró {Path} — se omite el seed de destinos de Bolivia.", jsonPath);
            return;
        }

        var raw = await File.ReadAllTextAsync(jsonPath, ct);
        var records = System.Text.Json.JsonSerializer.Deserialize<List<BoliviaCityRecord>>(raw) ?? [];

        // Dedup por (city, admin_name) case-insensitive — el JSON trae "Oruro" y "Camiri" duplicados con
        // población distinta; nos quedamos con la fila de mayor población como la más confiable.
        var deduped = records
            .Where(r => !string.IsNullOrWhiteSpace(r.City) && !string.IsNullOrWhiteSpace(r.AdminName))
            .GroupBy(r => $"{r.City.Trim().ToLowerInvariant()}||{r.AdminName.Trim().ToLowerInvariant()}")
            .Select(g => g.OrderByDescending(ParsePopulation).First())
            .ToList();

        // País — Bolivia.
        var country = await db.Destinations.FirstOrDefaultAsync(d => d.Type == DestinationType.COUNTRY && d.Name == "Bolivia", ct);
        if (country is null)
        {
            country = new Destination { Id = Guid.NewGuid(), Name = "Bolivia", Type = DestinationType.COUNTRY };
            db.Destinations.Add(country);
        }

        // Departamentos (Region) — un nombre por cada admin_name único, tal como viene en el JSON
        // (conserva "El Beni" en vez de normalizarlo a "Beni" — ver reporte de la sesión).
        var departmentNames = deduped.Select(r => r.AdminName.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var existingRegions = await db.Destinations
            .Where(d => d.Type == DestinationType.REGION && d.ParentId == country.Id)
            .ToListAsync(ct);
        var regionByName = existingRegions.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var deptName in departmentNames)
        {
            if (regionByName.ContainsKey(deptName)) continue;
            var region = new Destination { Id = Guid.NewGuid(), Name = deptName, Type = DestinationType.REGION, ParentId = country.Id };
            db.Destinations.Add(region);
            regionByName[deptName] = region;
        }

        // Ciudades — bajo su departamento correspondiente.
        var regionIds = regionByName.Values.Select(r => r.Id).ToList();
        var existingCityKeys = new HashSet<string>(
            (await db.Destinations
                .Where(d => d.Type == DestinationType.CITY && d.ParentId != null && regionIds.Contains(d.ParentId!.Value))
                .Select(d => new { d.Name, d.ParentId })
                .ToListAsync(ct))
            .Select(d => $"{d.ParentId}||{d.Name.Trim().ToLowerInvariant()}"));

        var citiesAdded = 0;
        foreach (var record in deduped)
        {
            var region = regionByName[record.AdminName.Trim()];
            var cityName = record.City.Trim();
            var key = $"{region.Id}||{cityName.ToLowerInvariant()}";
            if (existingCityKeys.Contains(key)) continue;

            db.Destinations.Add(new Destination { Id = Guid.NewGuid(), Name = cityName, Type = DestinationType.CITY, ParentId = region.Id });
            existingCityKeys.Add(key);
            citiesAdded++;
        }

        logger.LogInformation(
            "Seed: Bolivia → {Regions} departamentos, {Cities} ciudades nuevas ({Total} en el JSON deduplicado).",
            departmentNames.Count, citiesAdded, deduped.Count);
    }

    private static int ParsePopulation(BoliviaCityRecord record) =>
        int.TryParse(record.Population, out var population) ? population : 0;

    private sealed record BoliviaCityRecord(
        [property: JsonPropertyName("city")] string City,
        [property: JsonPropertyName("admin_name")] string AdminName,
        [property: JsonPropertyName("population")] string? Population);
}
