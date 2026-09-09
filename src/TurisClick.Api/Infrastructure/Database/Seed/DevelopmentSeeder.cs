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

        // Flush acá (no solo al final): CleanupDummyTestDestinationsAsync necesita poder consultar "La
        // Paz" ya persistida — en una base recién creada, SeedBoliviaDestinationsAsync todavía no la
        // guardó, solo la dejó trackeada en memoria.
        await db.SaveChangesAsync(ct);

        await CleanupDummyTestDestinationsAsync(db, logger, ct);

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

    /// <summary>
    /// Nombres que solo puede tener un destino generado por un test/Postman — ningún destino real de
    /// Bolivia (sembrado desde bolivia-cities.json) empieza con ninguno de estos prefijos seguidos de
    /// guion. Cubre los cuatro folders de la colección Postman que crean su propia jerarquía de
    /// destinos (Destinations, Experiences, Packages, Reservations): todos usan la plantilla
    /// "{Ciudad|Región|País}-{prefijoDeFolder}-{sufijo}" o "{Ciudad|Región|País}-{sufijo}".
    /// </summary>
    private static readonly string[] DummyDestinationNamePrefixes = ["Ciudad-", "País-", "Región-"];

    /// <summary>
    /// Limpieza idempotente de destinos "dummy" que quedan de corridas de Postman/Newman o pruebas
    /// manuales anteriores. Antes de borrar un destino CITY dummy, reasigna cualquier Experience/Package
    /// que lo referencie a un destino real (La Paz) — esos productos pueden tener reservas/pagos reales
    /// encima (creados en pruebas manuales previas) y no deben perderse ni romperse por una FK faltante.
    /// No hace nada si no encuentra destinos con ese patrón de nombre (corrida ya limpia).
    /// </summary>
    private static async Task CleanupDummyTestDestinationsAsync(TurisClickDbContext db, ILogger logger, CancellationToken ct)
    {
        var allDestinations = await db.Destinations.ToListAsync(ct);
        var dummyDestinations = allDestinations
            .Where(d => DummyDestinationNamePrefixes.Any(prefix => d.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        if (dummyDestinations.Count == 0)
            return;

        var dummyCityIds = dummyDestinations
            .Where(d => d.Type == DestinationType.CITY)
            .Select(d => d.Id)
            .ToHashSet();

        var reassignedExperiences = 0;
        var reassignedPackages = 0;
        var reassignedConversations = 0;

        if (dummyCityIds.Count > 0)
        {
            var realCity = await db.Destinations.FirstOrDefaultAsync(d => d.Type == DestinationType.CITY && d.Name == "La Paz", ct);
            if (realCity is null)
            {
                // No debería pasar en un flujo normal (el seed de Bolivia ya corrió y se flusheó antes
                // de llamar acá) — pero si alguien borró "La Paz" a mano, es más seguro postergar la
                // limpieza que reasignar productos reales a un destino inventado.
                logger.LogWarning(
                    "Seed: se encontraron {Count} destinos dummy pero no existe 'La Paz' como destino real todavía — se omite la limpieza en esta corrida.",
                    dummyDestinations.Count);
                return;
            }

            var experiencesToReassign = await db.Experiences.Where(e => dummyCityIds.Contains(e.DestinationId)).ToListAsync(ct);
            foreach (var experience in experiencesToReassign)
                experience.DestinationId = realCity.Id;
            reassignedExperiences = experiencesToReassign.Count;

            var packagesToReassign = await db.Packages.Where(p => dummyCityIds.Contains(p.DestinationId)).ToListAsync(ct);
            foreach (var package in packagesToReassign)
                package.DestinationId = realCity.Id;
            reassignedPackages = packagesToReassign.Count;

            // Oleada 5 sumó una tercera FK a `destinations` (ai_conversations.preferred_destination_id,
            // RESTRICT): sin reasignarla, borrar una ciudad dummy que alguna conversación de IA usó como
            // destino preferido rompe el arranque entero de la app en Development. Se reasigna al mismo
            // destino real que sus productos, así la conversación y su itinerario quedan coherentes.
            var conversationsToReassign = await db.AiConversations
                .Where(c => c.PreferredDestinationId != null && dummyCityIds.Contains(c.PreferredDestinationId!.Value))
                .ToListAsync(ct);
            foreach (var conversation in conversationsToReassign)
                conversation.PreferredDestinationId = realCity.Id;
            reassignedConversations = conversationsToReassign.Count;
        }

        // Orden FK-safe: CITY (hijos) primero, después REGION, después COUNTRY — mismo criterio que
        // "Orden de creación de tablas" de database-design.md, pero a la inversa para el borrado.
        foreach (var destination in dummyDestinations.OrderBy(TypeDeletionRank))
            db.Destinations.Remove(destination);

        logger.LogInformation(
            "Seed: limpieza de destinos dummy → {Deleted} destinos eliminados; {Experiences} experiencias, {Packages} paquetes y {Conversations} conversaciones de IA reasignados a un destino real (La Paz).",
            dummyDestinations.Count, reassignedExperiences, reassignedPackages, reassignedConversations);
    }

    private static int TypeDeletionRank(Destination destination) => destination.Type switch
    {
        DestinationType.CITY => 0,
        DestinationType.REGION => 1,
        _ => 2
    };

    private static int ParsePopulation(BoliviaCityRecord record) =>
        int.TryParse(record.Population, out var population) ? population : 0;

    private sealed record BoliviaCityRecord(
        [property: JsonPropertyName("city")] string City,
        [property: JsonPropertyName("admin_name")] string AdminName,
        [property: JsonPropertyName("population")] string? Population);
}
