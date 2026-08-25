using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace TurisClick.Api.Shared.Exceptions;

/// <summary>
/// Único punto de traducción de excepciones a respuestas HTTP (ProblemDetails, RFC 7807).
/// Evita try/catch repetidos en cada Controller — ver docs/backend-architecture.md, punto 4.
/// </summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            NotFoundAppException => (StatusCodes.Status404NotFound, "Recurso no encontrado"),
            ConflictAppException => (StatusCodes.Status409Conflict, "Conflicto"),
            ForbiddenAppException => (StatusCodes.Status403Forbidden, "Acceso denegado"),
            UnauthorizedAppException => (StatusCodes.Status401Unauthorized, "No autorizado"),
            ValidationAppException => (StatusCodes.Status400BadRequest, "Solicitud inválida"),
            GoneAppException => (StatusCodes.Status410Gone, "Recurso ya no disponible"),
            _ => (StatusCodes.Status500InternalServerError, "Error interno del servidor")
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Excepción no controlada en {Path}", httpContext.Request.Path);
        else
            logger.LogWarning("{ExceptionType} en {Path}: {Message}", exception.GetType().Name, httpContext.Request.Path, exception.Message);

        httpContext.Response.StatusCode = status;

        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = exception.Message,
            Instance = httpContext.Request.Path
        }, cancellationToken);

        return true;
    }
}
