using System.Text.RegularExpressions;

namespace TurisClick.Api.Shared.Validation;

/// <summary>
/// Reglas de validación por tipo de dato (docs/auditoria-validaciones.md, §7.1). Cada regla recibe el valor ya
/// normalizado y devuelve el mensaje de error en español, o <c>null</c> si es válido. Los atributos de
/// <see cref="Attributes"/> y los servicios usan las mismas funciones.
/// </summary>
public static partial class FieldRules
{
    // ── R-01 · nombre de empresa ──────────────────────────────────────────────────────────────────────────

    [GeneratedRegex(@"^[\p{L}\p{M}0-9 .,&'’\-()/+°–—]+$")]
    private static partial Regex OrganizationNameChars();

    public static string? OrganizationName(string? value, string label = "El nombre de la empresa")
    {
        if (string.IsNullOrEmpty(value)) return $"{label} es obligatorio.";
        if (TextNormalizer.HasForbiddenChars(value)) return $"{label} tiene caracteres invisibles o de control.";
        if (value.Length is < ValidationLimits.OrganizationNameMin or > ValidationLimits.OrganizationNameMax)
            return $"{label} debe tener entre {ValidationLimits.OrganizationNameMin} y {ValidationLimits.OrganizationNameMax} caracteres (tiene {value.Length}).";
        if (TextNormalizer.CountLetters(value) < 2) return $"{label} debe incluir al menos dos letras.";
        var invalid = FirstInvalidChar(value, OrganizationNameChars());
        if (invalid is not null) return $"{label} tiene un carácter no permitido: «{invalid}».";
        if (TextNormalizer.HasExcessiveRepetition(value))
            return $"{label} repite un mismo carácter más de {ValidationLimits.MaxRepeatedChars} veces seguidas.";
        return null;
    }

    // ── R-03 · NIT ────────────────────────────────────────────────────────────────────────────────────────

    public static class Nit
    {
        public static string? Normalize(string? value) =>
            value is null ? null : Regex.Replace(value, @"[\s.\-]", string.Empty);

        public static string? Validate(string? normalized)
        {
            if (string.IsNullOrEmpty(normalized)) return "El NIT es obligatorio.";
            if (!normalized.All(char.IsAsciiDigit)) return "El NIT sólo puede tener números.";
            if (normalized.Length is < ValidationLimits.NitMinDigits or > ValidationLimits.NitMaxDigits)
                return $"El NIT debe tener entre {ValidationLimits.NitMinDigits} y {ValidationLimits.NitMaxDigits} dígitos (tiene {normalized.Length}).";
            if (normalized.Distinct().Count() == 1) return "El NIT no puede ser un mismo dígito repetido.";
            return null;
        }
    }

    // ── R-04 · correo ─────────────────────────────────────────────────────────────────────────────────────

    public static partial class Email
    {
        public static string? Normalize(string? value) => value?.Trim().ToLowerInvariant();

        [GeneratedRegex(@"^[A-Za-z0-9!#$%&'*+/=?^_`{|}~\-]+(\.[A-Za-z0-9!#$%&'*+/=?^_`{|}~\-]+)*$")]
        private static partial Regex LocalPart();

        [GeneratedRegex(@"^[a-z0-9]([a-z0-9\-]*[a-z0-9])?$", RegexOptions.IgnoreCase)]
        private static partial Regex DomainLabel();

        [GeneratedRegex(@"^[a-z]{2,}$", RegexOptions.IgnoreCase)]
        private static partial Regex TopLevelDomain();

        public static string? Validate(string? normalized, string label = "El correo")
        {
            if (string.IsNullOrEmpty(normalized)) return $"{label} es obligatorio.";
            if (normalized.Any(char.IsWhiteSpace)) return $"{label} no puede tener espacios.";
            if (normalized.Any(ch => ch > 127))
                return $"{label} sólo puede tener letras sin tildes, números y los signos . _ - +.";
            if (normalized.Length > ValidationLimits.EmailMax)
                return $"{label} puede tener hasta {ValidationLimits.EmailMax} caracteres (tiene {normalized.Length}).";
            var at = normalized.LastIndexOf('@');
            if (at <= 0 || at == normalized.Length - 1 || normalized.IndexOf('@') != at)
                return $"{label} no tiene un formato válido (falta la @ o hay más de una).";
            var local = normalized[..at];
            var domain = normalized[(at + 1)..];
            if (local.Length > ValidationLimits.EmailLocalPartMax)
                return $"La parte antes de la @ puede tener hasta {ValidationLimits.EmailLocalPartMax} caracteres.";
            if (!LocalPart().IsMatch(local)) return $"{label} no tiene un formato válido antes de la @.";
            var labels = domain.Split('.');
            if (labels.Length < 2 || labels.Any(l => l.Length is 0 or > ValidationLimits.EmailDomainLabelMax || !DomainLabel().IsMatch(l))
                || !TopLevelDomain().IsMatch(labels[^1]))
                return $"{label} no tiene un dominio válido (por ejemplo, empresa.com.bo).";
            return null;
        }
    }

    // ── R-05 · teléfono de contacto ───────────────────────────────────────────────────────────────────────

    public static partial class Phone
    {
        [GeneratedRegex(@"^[234](?!50[1-9]|80\d|90\d|911)[2-689]\d{6}$")]
        private static partial Regex BolivianLandline();

        [GeneratedRegex(@"^[567]\d{7}$")]
        private static partial Regex BolivianMobile();

        [GeneratedRegex(@"^800\d{6}$")]
        private static partial Regex BolivianTollFree();

        [GeneratedRegex(@"^\+[1-9]\d{6,14}$")]
        private static partial Regex International();

        /// <summary>
        /// Normaliza y valida. Devuelve el número en formato internacional (<c>+591…</c> para Bolivia) o un
        /// mensaje de error. Vacío ⇒ (null, null): el teléfono es opcional.
        /// </summary>
        public static (string? Value, string? Error) Parse(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return (null, null);
            var compact = Regex.Replace(value.Trim(), @"[\s.()\-]", string.Empty);
            if (compact.Any(char.IsLetter)) return (null, "El teléfono no puede tener letras.");
            if (compact.Any(ch => !char.IsAsciiDigit(ch) && ch != '+') || compact.LastIndexOf('+') > 0)
                return (null, "El teléfono sólo puede tener números, espacios, guiones y el + inicial.");

            string national;
            if (compact.StartsWith("+591")) national = compact[4..];
            else if (compact.StartsWith('+'))
                return International().IsMatch(compact)
                    ? (compact, null)
                    : (null, "El número internacional no es válido: escribí el + y el código de país, por ejemplo +54 11 1234 5678.");
            else if (compact.StartsWith("00"))
                return (null, "Para un número del exterior escribí el + en lugar de 00, por ejemplo +54 11 1234 5678.");
            else if (compact.StartsWith("591") && compact.Length is 11 or 12) national = compact[3..];
            else if (compact.StartsWith('0'))
                return (null, "Escribí el número sin el 0 ni el código de operador.");
            else national = compact;

            if (BolivianLandline().IsMatch(national) || BolivianMobile().IsMatch(national) || BolivianTollFree().IsMatch(national))
                return ("+591" + national, null);
            if (national.Length is not (8 or 9))
                return (null, "Los números de Bolivia tienen 8 dígitos (o 9 las líneas 800).");
            return (null, "No es un número de Bolivia válido: los fijos empiezan con 2, 3 o 4 y los celulares con 5, 6 o 7.");
        }
    }

    // ── R-06 · nombres de personas ────────────────────────────────────────────────────────────────────────

    [GeneratedRegex(@"^\p{L}[\p{L}\p{M} '’\-.]*$")]
    private static partial Regex PersonNameChars();

    public static string? PersonName(string? value, string label)
    {
        if (string.IsNullOrEmpty(value)) return $"{label} es obligatorio.";
        if (TextNormalizer.HasForbiddenChars(value)) return $"{label} tiene caracteres invisibles o de control.";
        if (value.Length is < ValidationLimits.PersonNameMin or > ValidationLimits.PersonNameMax)
            return $"{label} debe tener entre {ValidationLimits.PersonNameMin} y {ValidationLimits.PersonNameMax} caracteres.";
        if (!PersonNameChars().IsMatch(value))
            return $"{label} sólo puede tener letras, espacios, apóstrofes, guiones y puntos, y debe empezar con una letra.";
        return null;
    }

    // ── R-07 · nombre de categoría ────────────────────────────────────────────────────────────────────────

    [GeneratedRegex(@"^[\p{L}\p{M}0-9 &\-'’,./()]+$")]
    private static partial Regex CategoryNameChars();

    public static string? CategoryName(string? value)
    {
        const string label = "El nombre de la categoría";
        if (string.IsNullOrEmpty(value)) return $"{label} es obligatorio.";
        if (TextNormalizer.HasForbiddenChars(value)) return $"{label} tiene caracteres invisibles o de control.";
        if (value.Length is < ValidationLimits.CategoryNameMin or > ValidationLimits.CategoryNameMax)
            return $"{label} debe tener entre {ValidationLimits.CategoryNameMin} y {ValidationLimits.CategoryNameMax} caracteres (tiene {value.Length}).";
        if (TextNormalizer.CountLetters(value) < 2) return $"{label} debe incluir al menos dos letras.";
        var invalid = FirstInvalidChar(value, CategoryNameChars());
        if (invalid is not null) return $"{label} tiene un carácter no permitido: «{invalid}».";
        if (TextNormalizer.HasExcessiveRepetition(value))
            return $"{label} repite un mismo carácter más de {ValidationLimits.MaxRepeatedChars} veces seguidas.";
        return null;
    }

    // ── textos libres (descripciones, motivos) ────────────────────────────────────────────────────────────

    public static string? PlainText(string? value, string label, int max, int min = 0, bool required = false, bool multiLine = false)
    {
        if (string.IsNullOrEmpty(value))
            return required ? $"{label} es obligatorio." : null;
        if (TextNormalizer.HasForbiddenChars(value, allowNewLines: multiLine))
            return $"{label} tiene caracteres invisibles o de control.";
        if (value.Length < min) return $"{label} debe tener al menos {min} caracteres.";
        if (value.Length > max) return $"{label} puede tener hasta {max} caracteres (tiene {value.Length}).";
        return null;
    }

    // ── R-11 · registro o licencia para vender vuelos ─────────────────────────────────────────────────────

    [GeneratedRegex(@"^[\p{L}\p{M}0-9 \-./°]+$")]
    private static partial Regex RegistrationChars();

    public static string? FlightSellerRegistration(string? value)
    {
        const string label = "El registro o la licencia";
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length is < ValidationLimits.FlightSellerRegistrationMin or > ValidationLimits.FlightSellerRegistrationMax)
            return $"{label} debe tener entre {ValidationLimits.FlightSellerRegistrationMin} y {ValidationLimits.FlightSellerRegistrationMax} caracteres.";
        if (!RegistrationChars().IsMatch(value)) return $"{label} sólo puede tener letras, números, espacios y los signos - . / °.";
        return null;
    }

    private static string? FirstInvalidChar(string value, Regex allowed)
    {
        foreach (var ch in value)
            if (!allowed.IsMatch(ch.ToString())) return ch.ToString();
        return null;
    }
}
