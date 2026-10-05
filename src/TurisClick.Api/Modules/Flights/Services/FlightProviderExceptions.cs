namespace TurisClick.Api.Modules.Flights.Services;

/// <summary>
/// Fallas de un proveedor aéreo, separadas por lo que el llamador tiene que HACER con ellas, no por el
/// código HTTP que las produjo. Mismo criterio que el módulo de IA: una excepción por decisión.
/// </summary>
public abstract class FlightProviderException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>No se pudo hablar con el proveedor: red caída, timeout, 5xx. Se puede reintentar.</summary>
public class FlightProviderUnavailableException(string message, Exception? inner = null)
    : FlightProviderException(message, inner);

/// <summary>El proveedor respondió, pero lo que mandamos no sirve (4xx de validación). Reintentar igual no arregla nada.</summary>
public class FlightProviderRequestException(string message, int statusCode, string? providerCode = null, Exception? inner = null)
    : FlightProviderException(message, inner)
{
    public int StatusCode { get; } = statusCode;

    /// <summary>Código del proveedor, útil para distinguir "oferta vencida" de "datos de pasajero inválidos".</summary>
    public string? ProviderCode { get; } = providerCode;
}

/// <summary>Credenciales rechazadas (401/403). Es un problema de configuración, no del turista.</summary>
public class FlightProviderAuthException(string message, Exception? inner = null)
    : FlightProviderException(message, inner);

/// <summary>Se superó el límite de llamadas (429). El llamador decide si espera o degrada.</summary>
public class FlightProviderRateLimitException(string message, TimeSpan? retryAfter = null, Exception? inner = null)
    : FlightProviderException(message, inner)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>La oferta ya no existe o venció. Hay que volver a buscar, no reintentar lo mismo.</summary>
public class FlightOfferExpiredException(string message, Exception? inner = null)
    : FlightProviderException(message, inner);

/// <summary>El proveedor contestó algo que no se puede interpretar. Nunca se adivina el contenido.</summary>
public class FlightProviderResponseException(string message, Exception? inner = null)
    : FlightProviderException(message, inner);
