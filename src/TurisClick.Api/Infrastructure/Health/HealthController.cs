using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;

namespace TurisClick.Api.Infrastructure.Health;

/// <summary>
/// Estado de la base contra la que corre este proceso: a qué base está conectado de verdad y qué
/// migraciones tiene aplicadas.
///
/// Existe por el incidente del 7 de octubre de 2026: el backend V2 se desplegó contra una base con
/// migraciones pendientes y la única forma de detectarlo desde afuera era leer el error crudo de
/// PostgreSQL en una respuesta 500. El workflow de deploy lee este endpoint antes de desplegar (compara
/// las migraciones aplicadas con las del código a desplegar) y después (no debe quedar ninguna pendiente).
///
/// Anónimo a propósito, porque lo consulta el pipeline. Expone sólo metadatos del esquema: ningún dato,
/// ningún host, usuario ni connection string, y si la base no responde no dice por qué.
/// </summary>
[ApiController]
[Route("api/health")]
[AllowAnonymous]
public class HealthController(TurisClickDbContext db, ILogger<HealthController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HealthResponse>> Get(CancellationToken ct)
    {
        HealthResponse response;
        try
        {
            // current_database() lo responde el servidor: prueba a qué base llegó la conexión, no lo que
            // dice la connection string.
            var database = await db.Database
                .SqlQuery<string>($"SELECT current_database() AS \"Value\"")
                .SingleAsync(ct);
            var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
            var pending = db.Database.GetMigrations().Except(applied).Count();

            response = new HealthResponse
            {
                Status = pending == 0 ? HealthStatus.Healthy : HealthStatus.PendingMigrations,
                Database = database,
                AppliedMigrations = applied.Count,
                LatestMigration = applied.LastOrDefault(),
                PendingMigrations = pending
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "El chequeo de salud no pudo consultar la base");
            response = new HealthResponse { Status = HealthStatus.DatabaseUnavailable };
        }

        return response.Status == HealthStatus.Healthy
            ? Ok(response)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }
}

public static class HealthStatus
{
    public const string Healthy = "HEALTHY";
    public const string PendingMigrations = "PENDING_MIGRATIONS";
    public const string DatabaseUnavailable = "DATABASE_UNAVAILABLE";
}

public class HealthResponse
{
    public string Status { get; init; } = HealthStatus.DatabaseUnavailable;
    public string? Database { get; init; }
    public int AppliedMigrations { get; init; }
    public string? LatestMigration { get; init; }
    public int PendingMigrations { get; init; }
}
