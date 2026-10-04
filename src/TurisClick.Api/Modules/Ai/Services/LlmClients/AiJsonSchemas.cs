using System.Text.Json.Nodes;

namespace TurisClick.Api.Modules.Ai.Services.LlmClients;

/// <summary>
/// Esquemas JSON que se le pasan a Ollama en `format` (structured outputs, Ollama ≥ 0.5). El modelo no
/// devuelve texto libre que después haya que adivinar: devuelve exactamente esta forma, y aun así el
/// backend valida todo contra Postgres antes de persistir nada.
///
/// Los esquemas describen la FORMA, no la verdad: que venga un "destination" no significa que exista; eso
/// lo decide el catálogo (ver FallbackAiModelClient.Sanitize y AiConversationService).
/// </summary>
public static class AiJsonSchemas
{
    private static JsonObject Nullable(string type) => new() { ["type"] = new JsonArray(type, "null") };

    private static JsonObject ArrayOfStrings() => new()
    {
        ["type"] = "array",
        ["items"] = new JsonObject { ["type"] = "string" },
    };

    /// <summary>
    /// Todas las propiedades van en "required", siempre. Con un "required" parcial el modelo puede
    /// OMITIR la clave, y omitir no es lo mismo que responder null: medido con qwen2.5:7b-instruct, los
    /// campos opcionales (duración, viajeros, fechas) venían ausentes incluso cuando el mensaje los
    /// decía, porque omitirlos es el camino más corto que la gramática permite. Con todas requeridas y
    /// tipos nullable, el modelo tiene que decidir y escribir un valor o null — y los acierta.
    /// Ver docs/ai-evaluation.md, "Historial de mediciones".
    /// </summary>
    private static JsonObject Object(JsonObject properties) => new()
    {
        ["type"] = "object",
        ["properties"] = properties,
        ["required"] = new JsonArray([.. properties.Select(p => (JsonNode)p.Key!)]),
    };

    /// <summary>UC-AI-01 — señales del último mensaje del turista.</summary>
    public static JsonNode Extraction { get; } = Object(
        new JsonObject
        {
            ["destination"] = Nullable("string"),
            ["categories"] = ArrayOfStrings(),
            ["startDate"] = Nullable("string"),
            ["endDate"] = Nullable("string"),
            ["durationDays"] = Nullable("integer"),
            ["travelers"] = Nullable("integer"),
            ["budgetAmount"] = Nullable("number"),
            ["budgetCurrency"] = Nullable("string"),
            ["budgetIsPerPerson"] = new JsonObject { ["type"] = "boolean" },
            ["restrictionsNotes"] = Nullable("string"),
            ["travelPace"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["enum"] = new JsonArray("RELAXED", "BALANCED", "INTENSE", null) },
        });

    public static JsonNode Clarification { get; } = Object(
        new JsonObject { ["reply"] = new JsonObject { ["type"] = "string" } });

    /// <summary>UC-AI-03/04/05 — qué candidato va en qué día. Los ids sólo pueden ser de los candidatos ofrecidos.</summary>
    public static JsonNode Composition { get; } = Object(
        new JsonObject
        {
            ["title"] = Nullable("string"),
            ["items"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = Object(
                    new JsonObject
                    {
                        ["day"] = new JsonObject { ["type"] = "integer" },
                        ["productType"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("EXPERIENCE", "PACKAGE") },
                        ["productId"] = new JsonObject { ["type"] = "string" },
                        ["availabilityId"] = Nullable("string"),
                    }),
            },
            ["explanation"] = new JsonObject { ["type"] = "string" },
        });

    /// <summary>UC-AI-05 — clasificación del ajuste pedido sobre la propuesta vigente.</summary>
    public static JsonNode Modification { get; } = Object(
        new JsonObject
        {
            ["action"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray("NONE", "REMOVE", "REPLACE", "ADD", "REDUCE_BUDGET", "PREFER_PACKAGE"),
            },
            ["targetItemIds"] = ArrayOfStrings(),
            ["targetDays"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "integer" } },
            ["addCategories"] = ArrayOfStrings(),
        });

    public static JsonNode Explanation { get; } = Object(
        new JsonObject { ["explanation"] = new JsonObject { ["type"] = "string" } });
}
