namespace TurisClick.Api.Modules.Ai.Dtos;

/// <summary>Respuesta de UC-T-13 — el resultado de procesar un mensaje del turista.</summary>
public class SendMessageResponse
{
    /// <summary>Texto de respuesta del asistente — solo UI, nunca fuente de verdad de datos.</summary>
    public string AssistantMessage { get; set; } = string.Empty;

    public PreferencesResponse ParsedPreferences { get; set; } = new();

    /// <summary>true si todavía faltan campos obligatorios (destino, fechas-o-duración, viajeros) para poder buscar — decisión determinística del backend, no del LLM.</summary>
    public bool ClarificationNeeded { get; set; }

    public List<string> MissingInformation { get; set; } = [];

    /// <summary>Presente solo cuando hay suficiente información y se generó una propuesta (UC-AI-04).</summary>
    public ItineraryResponse? Itinerary { get; set; }

    /// <summary>Ej. candidatos que el modelo eligió pero se descartaron por no validar (UC-11 anti-hallucination), o proveedor de IA no disponible.</summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Qué se tomó del perfil del turista (onboarding) porque el mensaje no lo decía — ej. "Tus intereses:
    /// Naturaleza, Aventura". Lo que el turista pide en la conversación siempre tiene prioridad sobre esto.
    /// </summary>
    public List<string> ProfileHints { get; set; } = [];
}
