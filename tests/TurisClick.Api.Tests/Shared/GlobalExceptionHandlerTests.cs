using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using TurisClick.Api.Shared.Exceptions;
using Xunit;

namespace TurisClick.Api.Tests.Shared;

/// <summary>
/// Lo que el cliente ve cuando algo falla. El 7 de octubre de 2026 el backend en Azure respondía
/// `"detail": "42703: column u.must_change_password does not exist"` a cualquiera que intentara iniciar
/// sesión: el mensaje de la excepción viajaba tal cual en el 500. Estas pruebas fijan que un error
/// inesperado nunca vuelva a llevar texto interno, y que los errores de negocio sigan diciendo lo que pasó.
/// </summary>
public class GlobalExceptionHandlerTests
{
    /// <summary>Fragmentos que delatan internals: SQL, PostgreSQL, el driver, la pila o una connection string.</summary>
    private static readonly string[] InternalFragments =
    [
        "42703", "must_change_password", "does not exist", "column", "SELECT", "Npgsql", "Postgres",
        "Exception", " at ", ".cs:line", "Host=", "Username=", "Password=", "hunter2", "turisclick_db"
    ];

    [Fact]
    public async Task UnErrorDePostgresDevuelveUn500GenericoSinDetallesInternos()
    {
        var exception = new PostgresException(
            "column u.must_change_password does not exist", "ERROR", "ERROR", "42703");

        var (status, body, logs) = await HandleAsync(exception);

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        AssertNoInternals(body);
        Assert.Equal(GlobalExceptionHandler.GenericServerErrorDetail, body.GetProperty("detail").GetString());

        // El detalle no se pierde: va al log, con la excepción completa y el mismo traceId que recibe el cliente.
        var error = Assert.Single(logs, l => l.Level == LogLevel.Error);
        Assert.Same(exception, error.Exception);
        Assert.Contains(body.GetProperty("traceId").GetString()!, error.Message);
    }

    [Fact]
    public async Task UnaExcepcionConUnaConnectionStringEnElMensajeNoLaExpone()
    {
        var exception = new InvalidOperationException(
            "No se pudo abrir Host=turisclick-postgres-jpm.postgres.database.azure.com;Database=turisclick_db_v2;Username=turisclick_v2_app;Password=hunter2");

        var (status, body, _) = await HandleAsync(exception);

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        AssertNoInternals(body);
    }

    [Fact]
    public async Task UnDbUpdateExceptionNoPrevistoTampocoExponeElSql()
    {
        var exception = new DbUpdateException(
            "An error occurred while saving the entity changes.",
            new PostgresException("value too long for type character varying(60)", "ERROR", "ERROR", "22001"));

        var (status, body, _) = await HandleAsync(exception);

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        AssertNoInternals(body);
        Assert.DoesNotContain("varying", body.GetRawText());
    }

    [Fact]
    public async Task UnErrorInesperadoNoExponeElCodigoDeErrorDeNegocio()
    {
        var (_, body, _) = await HandleAsync(new InvalidOperationException("algo interno"));

        Assert.False(body.TryGetProperty("errorCode", out _));
    }

    [Fact]
    public async Task ElCuerpoIlegibleDevuelveSuCodigoSinElMensajeDeKestrel()
    {
        var exception = new BadHttpRequestException("Request body too large. The max request body size is 30000000 bytes.", 413);

        var (status, body, _) = await HandleAsync(exception);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, status);
        Assert.Equal(GlobalExceptionHandler.GenericBadRequestDetail, body.GetProperty("detail").GetString());
        Assert.DoesNotContain("30000000", body.GetRawText());
    }

    [Fact]
    public async Task LosErroresDeNegocioConservanSuMensajeYSuCodigo()
    {
        var (status, body, logs) = await HandleAsync(
            new ConflictAppException("No quedan lugares para esa fecha.", ErrorCodes.InsufficientCapacity));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Equal("No quedan lugares para esa fecha.", body.GetProperty("detail").GetString());
        Assert.Equal(ErrorCodes.InsufficientCapacity, body.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
        Assert.DoesNotContain(logs, l => l.Level == LogLevel.Error);
    }

    public static TheoryData<string, int> BusinessErrors => new()
    {
        { nameof(NotFoundAppException), StatusCodes.Status404NotFound },
        { nameof(ForbiddenAppException), StatusCodes.Status403Forbidden },
        { nameof(UnauthorizedAppException), StatusCodes.Status401Unauthorized },
        { nameof(ValidationAppException), StatusCodes.Status400BadRequest },
        { nameof(GoneAppException), StatusCodes.Status410Gone },
    };

    [Theory]
    [MemberData(nameof(BusinessErrors))]
    public async Task CadaErrorDeNegocioMantieneSuEstado(string type, int expected)
    {
        const string message = "Mensaje para el usuario.";
        Exception exception = type switch
        {
            nameof(NotFoundAppException) => new NotFoundAppException(message),
            nameof(ForbiddenAppException) => new ForbiddenAppException(message),
            nameof(UnauthorizedAppException) => new UnauthorizedAppException(message),
            nameof(ValidationAppException) => new ValidationAppException(message),
            _ => new GoneAppException(message),
        };

        var (status, body, _) = await HandleAsync(exception);

        Assert.Equal(expected, status);
        Assert.Equal("Mensaje para el usuario.", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task LaViolacionDeForeignKeySigueSiendoUn409SinNombresDeConstraint()
    {
        var exception = new DbUpdateException("save failed",
            new PostgresException("violates foreign key constraint \"FK_x\"", "ERROR", "ERROR", "23503"));

        var (status, body, _) = await HandleAsync(exception);

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.DoesNotContain("FK_x", body.GetRawText());
    }

    private static void AssertNoInternals(JsonElement body)
    {
        var raw = body.GetRawText();
        foreach (var fragment in InternalFragments)
            Assert.DoesNotContain(fragment, raw, StringComparison.OrdinalIgnoreCase);

        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
    }

    private static async Task<(int Status, JsonElement Body, List<LogEntry> Logs)> HandleAsync(Exception exception)
    {
        var logger = new ListLogger();
        var handler = new GlobalExceptionHandler(logger);
        var context = new DefaultHttpContext { TraceIdentifier = "0HTEST:00000001" };
        context.Request.Path = "/api/auth/login";
        context.Request.Method = "POST";
        context.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);
        Assert.True(handled);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, document.RootElement.Clone(), logger.Entries);
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class ListLogger : ILogger<GlobalExceptionHandler>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }
}
