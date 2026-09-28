namespace TurisClick.Api.Modules.Ai.Services;

/// <summary>
/// Ai:Provider selecciona la implementación de IAiModelClient (nunca hardcodeada — appsettings/env,
/// ver AiModuleExtensions). "Ollama" es el proveedor de desarrollo principal de este proyecto de tesis
/// (corre localmente, no depende de una API externa); "Deterministic" es el usado por defecto en
/// Testing/Newman para no depender de un LLM no determinístico.
/// </summary>
public class AiOptions
{
    public const string SectionName = "Ai";

    public string Provider { get; set; } = "Deterministic";

    public OllamaOptions Ollama { get; set; } = new();

    /// <summary>Máximo de candidatos (Experience + Package) que se le ofrecen al modelo por composición — nunca se manda el catálogo completo (sección 9).</summary>
    public int MaxCandidatesPerType { get; set; } = 8;

    /// <summary>
    /// Con un proveedor LLM, cada operación cae al cliente determinístico si el modelo falla (caído,
    /// timeout, JSON inválido o respuesta sin candidatos reales). Se puede apagar SOLO para medir al LLM
    /// crudo en el benchmark: en la app siempre va encendido.
    /// </summary>
    public bool FallbackToDeterministic { get; set; } = true;
}

public class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Ver docs/ai-model-selection.md: qwen2.5:7b-instruct es el principal; llama3.1:8b, la alternativa.</summary>
    public string Model { get; set; } = "qwen2.5:7b-instruct";

    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// 0 = salida lo más determinística posible. Un asistente que cambia de respuesta ante el mismo
    /// mensaje es imposible de testear y de defender en una tesis.
    /// </summary>
    public double Temperature { get; set; } = 0;

    /// <summary>Cuánto tiempo mantiene el modelo cargado en memoria entre requests (formato de Ollama).</summary>
    public string KeepAlive { get; set; } = "10m";

    /// <summary>
    /// Pedir la salida con un JSON Schema (Ollama ≥ 0.5) en vez de sólo "json": el modelo devuelve la forma
    /// exacta que esperamos. Si el servidor es viejo y lo rechaza, el cliente reintenta con "json".
    /// </summary>
    public bool UseJsonSchema { get; set; } = true;
}
