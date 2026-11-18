using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TurisClick.Api.Shared.Validation;

namespace TurisClick.Api.Shared.Exceptions;

/// <summary>
/// Único punto de traducción de excepciones a respuestas HTTP (ProblemDetails, RFC 7807).
/// Evita try/catch repetidos en cada Controller — ver docs/backend-architecture.md, punto 4.
/// </summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>Código SQLSTATE de Postgres para violación de foreign key (23503).</summary>
    private const string PostgresForeignKeyViolation = "23503";

    /// <summary>
    /// SQLSTATE de datos que la base rechaza por inválidos: unicidad (23505), CHECK (23514), texto demasiado
    /// largo (22001) y número fuera de rango (22003). Son errores del dato, no del servidor: nunca un 500.
    /// </summary>
    private const string PostgresUniqueViolation = "23505";
    private static readonly HashSet<string> PostgresInvalidData = ["23514", "22001", "22003"];

    public const string DuplicateDetail = "Ya existe un registro con esos datos.";
    public const string InvalidDataDetail = "Algún dato no cumple las reglas. Revisá el formulario e intentá de nuevo.";

    /// <summary>Índices únicos ⇒ campo del formulario, para ubicar el error si un servicio no lo tradujo antes.</summary>
    private static readonly Dictionary<string, string> UniqueConstraintFields = new(StringComparer.Ordinal)
    {
        ["uq_users_email"] = "email",
        ["uq_companies_legal_document"] = "legalDocument",
        ["uq_companies_legal_document_normalized"] = "legalDocument",
        ["uq_companies_name_key"] = "name",
        ["uq_categories_name"] = "name",
        ["uq_categories_name_key"] = "name",
    };

    public const string GenericServerErrorDetail =
        "Ocurrió un error inesperado. Si el problema persiste, informá el traceId de esta respuesta.";

    public const string GenericBadRequestDetail = "El servidor no pudo leer la solicitud.";

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
            ServiceUnavailableAppException => (StatusCodes.Status503ServiceUnavailable, "Servicio no disponible", exception.Message),
            NotImplementedYetAppException => (StatusCodes.Status501NotImplemented, "Todavía no disponible", exception.Message),

            // Red de seguridad genérica: si algún Service todavía no valida explícitamente una FK antes
            // de borrar/modificar (como sí hace DestinationService.DeleteAsync para experiences/packages),
            // esto evita que el mensaje crudo de Postgres/EF ("An error occurred while saving the entity
            // changes...", con detalles de constraint/tabla en el inner exception) llegue al cliente.
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresForeignKeyViolation } } =>
                (StatusCodes.Status409Conflict, "Conflicto",
                    "No se puede completar la operación porque el recurso está siendo utilizado por otros datos."),

            // Carrera entre la verificación del servicio y el INSERT/UPDATE: el índice único decide.
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresUniqueViolation } } =>
                (StatusCodes.Status409Conflict, "Conflicto", DuplicateDetail),

            // Un dato que la base rechaza (CHECK, largo, rango) es un dato inválido: 400 en español, no 500.
            DbUpdateException { InnerException: PostgresException pg } when PostgresInvalidData.Contains(pg.SqlState) =>
                (StatusCodes.Status400BadRequest, ValidationProblemFactory.Title, InvalidDataDetail),

            // Request mal formado que Kestrel/ASP.NET rechaza antes de llegar al Controller (cuerpo
            // ilegible, demasiado grande). Su mensaje describe el servidor, no el pedido: no se devuelve.
            BadHttpRequestException bad => (bad.StatusCode, "Solicitud inválida", GenericBadRequestDetail),

            // Todo lo demás es un error que nadie previó. Hasta la Oleada 15 se devolvía exception.Message, y
            // en Azure eso publicó el texto crudo de PostgreSQL ("42703: column u.must_change_password does
            // not exist") a cualquiera que intentara iniciar sesión. El detalle va sólo al log, con el traceId
            // que también recibe el cliente para poder encontrarlo.
            _ => (StatusCodes.Status500InternalServerError, "Error interno del servidor", GenericServerErrorDetail)
        };

        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        // Un 501 declarado no es una falla: es una funcionalidad que todavía no existe, y no merece un error en el log.
        if (status >= StatusCodes.Status500InternalServerError && exception is not NotImplementedYetAppException)
            logger.LogError(exception, "Excepción no controlada en {Method} {Path} (traceId {TraceId})",
                httpContext.Request.Method, httpContext.Request.Path, traceId);
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
        problem.Extensions["traceId"] = traceId;

        // Código legible por máquina cuando la excepción lo declara (ej. INSUFFICIENT_CAPACITY): permite
        // que el cliente decida qué ofrecer sin parsear el mensaje. Nunca se expone en un 500.
        if (status != StatusCodes.Status500InternalServerError
            && exception is IHasErrorCode { ErrorCode: { } errorCode })
        {
            problem.Extensions["errorCode"] = errorCode;
        }

        // Errores por campo: los declara la excepción o se deducen del índice único que se violó.
        var field = exception is IHasField { Field: { } f } ? f : UniqueViolationField(exception);
        if (field is not null && status is StatusCodes.Status400BadRequest or StatusCodes.Status409Conflict)
            problem.Extensions["errors"] = new Dictionary<string, string[]> { [field] = [detail] };

        if (exception is DbUpdateException { InnerException: PostgresException { SqlState: var state } })
        {
            if (state == PostgresUniqueViolation) problem.Extensions["errorCode"] = ErrorCodes.Duplicate;
            else if (PostgresInvalidData.Contains(state)) problem.Extensions["errorCode"] = ErrorCodes.ValidationFailed;
        }

        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }

    private static string? UniqueViolationField(Exception exception) =>
        exception is DbUpdateException { InnerException: PostgresException { SqlState: PostgresUniqueViolation, ConstraintName: { } name } }
        && UniqueConstraintFields.TryGetValue(name, out var field)
            ? field
            : null;
}
