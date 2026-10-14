using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TurisClick.Api.Infrastructure.Health;

/// <summary>
/// Salud de la app, pública y mínima: sólo dice si está sana. Existe por el incidente del 7 de octubre de
/// 2026, en el que el backend V2 corrió contra una base con migraciones pendientes y la única forma de
/// saberlo desde afuera era leer el error crudo de PostgreSQL en un 500.
///
/// Sana = la base responde, el código no ve migraciones pendientes y, si Database:ExpectedName está
/// configurado (en Production lo está: appsettings.Production.json), la base es esa.
///
/// El pipeline necesita además probar que la base es turisclick_db_v2 con exactamente las migraciones del
/// commit que despliega. Para eso manda <c>?expect=</c> con la huella que calcula del código
/// (DatabaseSchemaInspector.Fingerprint) y recibe sólo <c>schemaMatches</c>: verdadero o falso. La respuesta
/// nunca nombra la base ni las migraciones; el detalle está en GET /api/admin/health/schema, sólo ADMIN.
/// </summary>
[ApiController]
[Route("api/health")]
[AllowAnonymous]
public class HealthController(DatabaseSchemaInspector inspector) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PublicHealthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<PublicHealthResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PublicHealthResponse>> Get([FromQuery] string? expect, CancellationToken ct)
    {
        var snapshot = await inspector.InspectAsync(ct);
        var healthy = snapshot is { IsHealthy: true };

        var response = new PublicHealthResponse
        {
            Status = healthy ? HealthStatus.Healthy : HealthStatus.Unhealthy,
            SchemaMatches = string.IsNullOrEmpty(expect) ? null : snapshot is not null && FixedTimeEquals(expect, snapshot.Fingerprint)
        };

        return healthy ? Ok(response) : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a.ToLowerInvariant()), Encoding.UTF8.GetBytes(b));
}

/// <summary>Detalle del esquema para operar: a qué base está conectada la app y qué migraciones tiene.</summary>
[ApiController]
[Route("api/admin/health")]
[Authorize(Policy = "RequireAdmin")]
public class AdminHealthController(DatabaseSchemaInspector inspector) : ControllerBase
{
    [HttpGet("schema")]
    [ProducesResponseType<SchemaHealthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<SchemaHealthResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SchemaHealthResponse>> Schema(CancellationToken ct)
    {
        var snapshot = await inspector.InspectAsync(ct);
        if (snapshot is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new SchemaHealthResponse { Status = HealthStatus.Unhealthy });

        var response = new SchemaHealthResponse
        {
            Status = snapshot.IsHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy,
            Database = snapshot.Database,
            DatabaseMatchesExpected = snapshot.DatabaseMatches,
            AppliedMigrations = snapshot.AppliedMigrations.Count,
            LatestMigration = snapshot.AppliedMigrations.LastOrDefault(),
            PendingMigrations = snapshot.PendingMigrations,
            Fingerprint = snapshot.Fingerprint
        };

        return snapshot.IsHealthy ? Ok(response) : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }
}

public static class HealthStatus
{
    public const string Healthy = "HEALTHY";
    public const string Unhealthy = "UNHEALTHY";
}

public class PublicHealthResponse
{
    public string Status { get; init; } = HealthStatus.Unhealthy;

    /// <summary>Sólo presente si se mandó <c>?expect=</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SchemaMatches { get; init; }
}

public class SchemaHealthResponse
{
    public string Status { get; init; } = HealthStatus.Unhealthy;
    public string? Database { get; init; }
    public bool DatabaseMatchesExpected { get; init; }
    public int AppliedMigrations { get; init; }
    public string? LatestMigration { get; init; }
    public int PendingMigrations { get; init; }
    public string? Fingerprint { get; init; }
}
