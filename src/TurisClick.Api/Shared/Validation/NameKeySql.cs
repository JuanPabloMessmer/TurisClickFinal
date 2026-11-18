namespace TurisClick.Api.Shared.Validation;

/// <summary>
/// Expresiones SQL de las claves normalizadas. Son la única fuente para los índices únicos de la migración
/// <c>0016_Fase1_Validaciones</c> y para las consultas de los servicios: si cambiaran por separado, la API
/// podría aceptar un nombre que la base rechaza (o al revés).
///
/// Sólo usan funciones IMMUTABLE de PostgreSQL (btrim, regexp_replace, lower, translate), condición para un
/// índice por expresión, y no dependen de la extensión <c>unaccent</c>. Las vocales acentuadas se resuelven con
/// <c>translate</c> en mayúscula y en minúscula, así el resultado no depende de la configuración regional
/// (<c>lc_ctype</c>) de la base.
/// </summary>
public static class NameKeySql
{
    /// <summary>K1 de una columna de texto: equivalente SQL de <see cref="TextNormalizer.NameKey"/>.</summary>
    public static string NameKey(string column) =>
        $"translate(lower(regexp_replace(btrim({column}), '\\s+', ' ', 'g')), " +
        $"'{TextNormalizer.AccentedChars}', '{TextNormalizer.UnaccentedChars}')";

    /// <summary>NIT sin espacios, puntos ni guiones: equivalente SQL de <see cref="FieldRules.Nit.Normalize"/>.</summary>
    public static string NormalizedNit(string column) => $"regexp_replace({column}, '[\\s.\\-]', '', 'g')";
}
