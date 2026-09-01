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
}

public class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "llama3.2:3b";
    public int TimeoutSeconds { get; set; } = 30;
}
