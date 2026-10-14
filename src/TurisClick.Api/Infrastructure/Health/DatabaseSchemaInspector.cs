using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;

namespace TurisClick.Api.Infrastructure.Health;

/// <summary>
/// Lee de la base a la que el proceso está conectado de verdad (current_database(), lo responde el
/// servidor) qué migraciones tiene aplicadas, y lo compara con las que declara el código que corre.
/// Lo usan GET /api/health (público, mínimo) y GET /api/admin/health/schema (ADMIN, detallado).
/// </summary>
public class DatabaseSchemaInspector(TurisClickDbContext db, IConfiguration configuration, ILogger<DatabaseSchemaInspector> logger)
{
    /// <summary>Nombre de base esperado (Database:ExpectedName). Si está configurado y no coincide, la app no está sana.</summary>
    public const string ExpectedDatabaseKey = "Database:ExpectedName";

    public async Task<SchemaSnapshot?> InspectAsync(CancellationToken ct)
    {
        try
        {
            var database = await db.Database
                .SqlQuery<string>($"SELECT current_database() AS \"Value\"")
                .SingleAsync(ct);
            var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
            var pending = db.Database.GetMigrations().Except(applied).Count();
            var expected = configuration[ExpectedDatabaseKey];

            return new SchemaSnapshot(
                database,
                applied,
                pending,
                DatabaseMatches: string.IsNullOrWhiteSpace(expected) || string.Equals(expected, database, StringComparison.Ordinal),
                Fingerprint: Fingerprint(database, applied));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "No se pudo consultar el esquema de la base");
            return null;
        }
    }

    /// <summary>
    /// SHA-256 (hex en minúsculas) de la base y de las migraciones aplicadas, en orden, una por línea:
    /// <c>{database}\n{migration1}\n…\n{migrationN}</c>. El pipeline calcula el mismo valor con las migraciones
    /// del commit que despliega (.github/scripts/schema_preflight.py, función fingerprint) y lo manda para
    /// comparar: así verifica base y migraciones exactas sin que la respuesta pública las nombre.
    /// </summary>
    public static string Fingerprint(string database, IEnumerable<string> appliedMigrations)
    {
        var text = string.Join('\n', new[] { database }.Concat(appliedMigrations));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}

public sealed record SchemaSnapshot(
    string Database,
    IReadOnlyList<string> AppliedMigrations,
    int PendingMigrations,
    bool DatabaseMatches,
    string Fingerprint)
{
    public bool IsHealthy => PendingMigrations == 0 && DatabaseMatches;
}
