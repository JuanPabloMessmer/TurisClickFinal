using System.Globalization;
using System.Text;

namespace TurisClick.Api.Shared.Validation;

/// <summary>
/// Normalización de textos antes de validarlos y de guardarlos (docs/auditoria-validaciones.md, §6).
///
/// Validar y guardar el mismo valor normalizado es la regla: si el DTO validara el texto crudo y el servicio
/// guardara otro (como pasaba con <c>" a"</c>, que pasaba <c>MinLength(2)</c> y se guardaba <c>"a"</c>), la
/// validación no protegería lo que realmente llega a la base.
/// </summary>
public static class TextNormalizer
{
    /// <summary>Vocales acentuadas y su versión sin tilde. La ñ no está: es una letra distinta (Peña ≠ Pena).</summary>
    public const string AccentedChars = "ÁÀÂÄÃÉÈÊËÍÌÎÏÓÒÔÖÕÚÙÛÜÑáàâäãéèêëíìîïóòôöõúùûü";

    public const string UnaccentedChars = "aaaaaeeeeiiiiooooouuuuñaaaaaeeeeiiiiooooouuuu";

    /// <summary>Formas societarias que no distinguen a una empresa de otra al buscar nombres parecidos.</summary>
    private static readonly string[] LegalForms = ["srl", "sa", "ltda"];

    /// <summary>
    /// Texto de una línea: Unicode NFC, cualquier espacio (incluido el no separable y el tabulador) pasa a ser
    /// un espacio común, los espacios repetidos se colapsan y se recortan los extremos. <c>null</c> sigue siendo
    /// <c>null</c>. No elimina caracteres de control ni invisibles: los detecta <see cref="HasForbiddenChars"/>.
    /// </summary>
    public static string? SingleLine(string? value)
    {
        if (value is null) return null;
        var normalized = value.Normalize(NormalizationForm.FormC);
        var sb = new StringBuilder(normalized.Length);
        var pendingSpace = false;
        foreach (var ch in normalized)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }
            if (pendingSpace) sb.Append(' ');
            pendingSpace = false;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>Texto de varias líneas: NFC, saltos de línea unificados en <c>\n</c> y extremos recortados.</summary>
    public static string? MultiLine(string? value) =>
        value?.Normalize(NormalizationForm.FormC).Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    /// <summary>Vacío o sólo espacios pasa a <c>null</c>: un campo opcional sin contenido no se guarda como "".</summary>
    public static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>
    /// true si el texto tiene caracteres de control (salvo el salto de línea cuando se permite) o de formato
    /// invisibles (ancho cero, marcas de dirección, BOM). Esos caracteres no se ven en pantalla y sirven para
    /// esquivar la detección de duplicados o para inyectar contenido engañoso.
    /// </summary>
    public static bool HasForbiddenChars(string value, bool allowNewLines = false)
    {
        foreach (var ch in value)
        {
            if (allowNewLines && ch == '\n') continue;
            var category = char.GetUnicodeCategory(ch);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate
                or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Clave de unicidad (K1): texto de una línea, en minúsculas y sin tildes ni diéresis. Es la misma
    /// expresión que usan los índices únicos de la base (<see cref="NameKeySql"/>), así la regla de la API y la
    /// de PostgreSQL coinciden también ante altas simultáneas.
    /// </summary>
    public static string NameKey(string value)
    {
        var lower = (SingleLine(value) ?? string.Empty).ToLowerInvariant();
        var sb = new StringBuilder(lower.Length);
        foreach (var ch in lower)
        {
            var index = AccentedChars.IndexOf(ch);
            sb.Append(index >= 0 ? UnaccentedChars[index] : ch);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Clave de similitud (K2), sólo para advertir: la K1 sin signos de puntuación y sin la forma societaria
    /// final. "Andes Tours S.R.L.", "Andes Tours SRL" y "Andes Tours" comparten K2 aunque sean K1 distintas.
    /// </summary>
    public static string SimilarityKey(string value)
    {
        var key = NameKey(value);
        var sb = new StringBuilder(key.Length);
        foreach (var ch in key)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch == ' ') sb.Append(' ');
            // Los signos se eliminan sin dejar espacio: "S.R.L." queda "srl".
        }
        var words = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 1 && LegalForms.Contains(words[^1]))
            words.RemoveAt(words.Count - 1);
        return string.Join(' ', words);
    }

    /// <summary>true si algún carácter que no es dígito se repite más de <paramref name="max"/> veces seguidas.</summary>
    public static bool HasExcessiveRepetition(string value, int max = ValidationLimits.MaxRepeatedChars)
    {
        var run = 1;
        for (var i = 1; i < value.Length; i++)
        {
            if (value[i] == value[i - 1] && !char.IsDigit(value[i]))
            {
                run++;
                if (run > max) return true;
            }
            else run = 1;
        }
        return false;
    }

    /// <summary>Cantidad de letras (de cualquier alfabeto) del texto.</summary>
    public static int CountLetters(string value) => value.Count(char.IsLetter);
}
