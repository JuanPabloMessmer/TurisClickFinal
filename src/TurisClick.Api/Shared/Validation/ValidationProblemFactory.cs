using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace TurisClick.Api.Shared.Validation;

/// <summary>
/// Respuesta 400 de validación de modelo, en español y con claves iguales a las del formulario
/// (docs/auditoria-validaciones.md, §9.2):
///
/// <code>
/// { "title": "Hay datos para corregir.", "status": 400, "errorCode": "VALIDATION_FAILED",
///   "errors": { "legalDocument": ["El NIT debe tener entre 5 y 12 dígitos (tiene 4)."] }, "traceId": "…" }
/// </code>
///
/// Reemplaza la respuesta automática de <c>[ApiController]</c>, que llegaba en inglés
/// ("One or more validation errors occurred.", "The CompanyName field is required.") y con los nombres de las
/// propiedades de C#. Los mensajes por defecto de ASP.NET y de System.Text.Json que todavía queden en DTOs de
/// otros módulos se reemplazan por textos genéricos en español: nunca llega al cliente un mensaje técnico.
/// </summary>
public static partial class ValidationProblemFactory
{
    public const string Title = "Hay datos para corregir.";
    public const string ErrorCode = "VALIDATION_FAILED";
    public const string RequiredMessage = "Este campo es obligatorio.";
    public const string InvalidValueMessage = "El valor de este campo no es válido.";
    public const string UnreadableBodyMessage = "No pudimos leer los datos enviados. Revisá el formulario e intentá de nuevo.";

    /// <summary>Mensajes por defecto de DataAnnotations / System.Text.Json (todos en inglés).</summary>
    [GeneratedRegex(@"^(The |'.*' |A non-empty request body|The JSON value|JSON deserialization|The input does not contain|Could not|Unexpected|'.+' is an invalid)|\bfield\b|\bmust be\b|\bis required\b|\bis not a valid\b|could not be converted|LineNumber|BytePositionInLine|System\.|\$\.", RegexOptions.IgnoreCase)]
    private static partial Regex DefaultEnglishMessage();

    [GeneratedRegex(@"\bis required\b|\brequired\b|A non-empty request body", RegexOptions.IgnoreCase)]
    private static partial Regex RequiredEnglishMessage();

    public static IActionResult Create(ActionContext context)
    {
        var problem = new ValidationProblemDetails(ToErrors(context.ModelState))
        {
            Title = Title,
            Status = StatusCodes.Status400BadRequest,
            Instance = context.HttpContext.Request.Path,
        };
        problem.Extensions["errorCode"] = ErrorCode;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    }

    public static Dictionary<string, string[]> ToErrors(ModelStateDictionary modelState)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (key, entry) in modelState)
        {
            if (entry.Errors.Count == 0) continue;

            var field = ToFieldKey(key);
            var ours = new List<string>();
            var fallback = new List<string>();
            foreach (var error in entry.Errors)
            {
                var message = error.ErrorMessage;
                if (string.IsNullOrWhiteSpace(message))
                    fallback.Add(error.Exception is null ? InvalidValueMessage : UnreadableBodyMessage);
                else if (DefaultEnglishMessage().IsMatch(message))
                    fallback.Add(field.Length == 0 ? UnreadableBodyMessage
                        : RequiredEnglishMessage().IsMatch(message) ? RequiredMessage : InvalidValueMessage);
                else
                    ours.Add(message);
            }

            // Si una regla propia ya explicó el problema, el mensaje genérico (p. ej. el Required implícito de
            // las propiedades no anulables) sobra.
            var messages = ours.Count > 0 ? ours : fallback;
            if (!result.TryGetValue(field, out var list)) result[field] = list = [];
            foreach (var m in messages)
                if (!list.Contains(m)) list.Add(m);
        }
        return result.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.Ordinal);
    }

    /// <summary>
    /// "CompanyName" ⇒ "companyName"; "$.contactPhone" ⇒ "contactPhone"; "Items[0].Title" ⇒ "items[0].title";
    /// la clave del cuerpo completo ("", "$", "request") ⇒ "".
    /// </summary>
    public static string ToFieldKey(string key)
    {
        var k = key.StartsWith("$.") ? key[2..] : key == "$" ? string.Empty : key;
        if (k is "request" or "body") return string.Empty;
        return string.Join('.', k.Split('.').Select(part => part.Length == 0 ? part : char.ToLowerInvariant(part[0]) + part[1..]));
    }
}
