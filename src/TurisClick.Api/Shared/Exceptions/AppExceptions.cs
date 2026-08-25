namespace TurisClick.Api.Shared.Exceptions;

/// <summary>Recurso solicitado no existe. El GlobalExceptionHandler la mapea a 404.</summary>
public class NotFoundAppException(string message) : Exception(message);

/// <summary>Conflicto de negocio (ej. email duplicado, sin cupo). Se mapea a 409.</summary>
public class ConflictAppException(string message) : Exception(message);

/// <summary>El usuario autenticado no tiene permiso sobre el recurso (ej. UC-SYS-03). Se mapea a 403.</summary>
public class ForbiddenAppException(string message) : Exception(message);

/// <summary>Credenciales inválidas o token inválido/expirado. Se mapea a 401.</summary>
public class UnauthorizedAppException(string message) : Exception(message);

/// <summary>Regla de negocio inválida que no corresponde a una validación de ModelState. Se mapea a 400.</summary>
public class ValidationAppException(string message) : Exception(message);

/// <summary>
/// El recurso existió pero ya no es utilizable (ej. UC-T-08: un slot de disponibilidad cerrado o con
/// fecha vencida) — distinto de NotFound porque el recurso no es inexistente, quedó obsoleto. Se mapea a 410.
/// </summary>
public class GoneAppException(string message) : Exception(message);
