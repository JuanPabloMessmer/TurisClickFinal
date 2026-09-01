namespace TurisClick.Api.Modules.Ai.Services;

/// <summary>
/// El proveedor de IA no respondió (Ollama caído/inalcanzable, timeout). NUNCA debe tirar abajo la API
/// — AiConversationService la captura y devuelve un error funcional (ver docs de la sesión, sección 6).
/// </summary>
public class AiModelUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>El modelo respondió pero el JSON no se pudo parsear/validar ni siquiera tras un reintento (sección 7).</summary>
public class AiModelResponseException(string message, Exception? inner = null) : Exception(message, inner);
