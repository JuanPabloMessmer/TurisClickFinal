using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TurisClick.Api.Shared.Exceptions;

/// <summary>
/// Único punto de traducción de excepciones a respuestas HTTP (ProblemDetails, RFC 7807).
/// Evita try/catch repetidos en cada Controller — ver docs/backend-architecture.md, punto 4.
/// </summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>Código SQLSTATE de Postgres para violación de foreign key (23503).</summary>
    private const string PostgresForeignKeyViolation = "23503";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            NotFoundAppException => (StatusCodes.Status404NotFound, "Recurso no encontrado", exception.Message),
            ConflictAppException => (StatusCodes.Status409Conflict, "Conflicto", exception.Message),
            ForbiddenAppException => (StatusCodes.Status403Forbidden, "Acceso denegado", exception.Message),
            UnauthorizedAppException => (StatusCodes.Status401Unauthorized, "No autorizado", exception.Message),
            ValidationAppException => (StatusCodes.Status400BadRequest, "Solicitud inválida", exception.Message),
            GoneAppException => (StatusCodes.Status410Gone, "Recurso ya no disponible", exception.Message),

            // Red de seguridad genérica: si algún Service todavía no valida explícitamente una FK antes
            // de borrar/modificar (como sí hace DestinationService.DeleteAsync para experiences/packages),
            // esto evita que el mensaje crudo de Postgres/EF ("An error occurred while saving the entity
            // changes...", con detalles de constraint/tabla en el inner exception) llegue al cliente.
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresForeignKeyViolation } } =>
                (StatusCodes.Status409Conflict, "Conflicto",
                    "No se puede completar la operación porque el recurso está siendo utilizado por otros datos."),

            _ => (StatusCodes.Status500InternalServerError, "Error interno del servidor", exception.Message)
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Excepción no controlada en {Path}", httpContext.Request.Path);
        else
            logger.LogWarning("{ExceptionType} en {Path}: {Message}", exception.GetType().Name, httpContext.Request.Path, exception.Message);

        httpContext.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        // Código legible por máquina cuando la excepción lo declara (ej. INSUFFICIENT_CAPACITY): permite
        // que el cliente decida qué ofrecer sin parsear el mensaje. Nunca se expone en un 500.
        if (status != StatusCodes.Status500InternalServerError
            && exception is IHasErrorCode { ErrorCode: { } errorCode })
        {
            problem.Extensions["errorCode"] = errorCode;
        }

        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }
}
