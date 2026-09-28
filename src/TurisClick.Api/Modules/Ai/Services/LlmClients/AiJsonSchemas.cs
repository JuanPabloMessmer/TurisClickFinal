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

    private static JsonObject Object(JsonObject properties, params string[] required) => new()
    {
        ["type"] = "object",
        ["properties"] = properties,
        ["required"] = new JsonArray([.. required.Select(r => (JsonNode)r!)]),
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
        },
        "destination", "categories", "budgetIsPerPerson");

    public static JsonNode Clarification { get; } = Object(
        new JsonObject { ["reply"] = new JsonObject { ["type"] = "string" } },
        "reply");

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
                    },
                    "day", "productType", "productId"),
            },
            ["explanation"] = new JsonObject { ["type"] = "string" },
        },
        "items", "explanation");

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
        },
        "action", "targetItemIds", "targetDays", "addCategories");

    public static JsonNode Explanation { get; } = Object(
        new JsonObject { ["explanation"] = new JsonObject { ["type"] = "string" } },
        "explanation");
}
