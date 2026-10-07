using System.Globalization;

namespace TurisClick.Api.Modules.Reservations.Policies;

/// <summary>
/// Política de cancelación de un producto: cuánto se devuelve según cuántos días falten para el viaje.
///
/// Es un objeto de valor y se guarda como **texto** (`"30:100;15:50;0:0"`), por la misma razón que
/// `PackageFlightRule.AllowedOriginIatas`: son dos a seis tramos, y una tabla entera para eso agrega un
/// join a cada lectura sin agregar una sola garantía.
///
/// Lo que vuelve esta decisión importante no es el formato sino que el texto se **copia** a la reserva al
/// comprar. Si el operador cambia su política mañana, la persona que compró hoy conserva la que aceptó: un
/// contrato que cambia solo después de firmarlo no es un contrato.
/// </summary>
public sealed record CancellationPolicy(IReadOnlyList<CancellationTier> Tiers)
{
    public const int MaxTiers = 6;
    public const int MaxDaysBefore = 365;

    /// <summary>Cuántos caracteres puede ocupar la forma serializada; `MaxTiers` tramos entran holgados.</summary>
    public const int MaxLength = 60;

    /// <summary>
    /// Lo que la base acepta en la columna. Un tramo es `días:porcentaje`, separados por `;`. El CHECK no
    /// valida el orden ni la coherencia —eso lo hace <see cref="Validate"/>—, sólo que sea esta forma.
    /// </summary>
    public const string ColumnRegex = @"^[0-9]{1,3}:[0-9]{1,3}(;[0-9]{1,3}:[0-9]{1,3}){0,5}$";

    public string Serialize() =>
        string.Join(';', Tiers.Select(t => string.Create(
            CultureInfo.InvariantCulture, $"{t.MinDaysBefore}:{t.RefundPercentage}")));

    public static bool TryParse(string? value, out CancellationPolicy? policy)
    {
        policy = null;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var tiers = new List<CancellationTier>();

        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split(':');
            if (pieces.Length != 2) return false;
            if (!int.TryParse(pieces[0], NumberStyles.None, CultureInfo.InvariantCulture, out var days)) return false;
            if (!int.TryParse(pieces[1], NumberStyles.None, CultureInfo.InvariantCulture, out var percentage)) return false;

            tiers.Add(new CancellationTier(days, percentage));
        }

        if (tiers.Count == 0) return false;

        policy = new CancellationPolicy(tiers);
        return true;
    }

    public static CancellationPolicy Parse(string value) =>
        TryParse(value, out var policy) ? policy! : throw new FormatException($"Política de cancelación inválida: '{value}'.");

    /// <summary>
    /// Qué porcentaje corresponde devolver si se cancela con <paramref name="daysBeforeStart"/> días de
    /// anticipación. Los tramos se evalúan de mayor a menor, y gana el primero cuyo mínimo se alcance.
    ///
    /// **Si ningún tramo aplica, devuelve 0.** Es el default conservador: una política que empieza en "15
    /// días o más" no dice nada sobre cancelar la noche anterior, y en esa ausencia no se puede inventar un
    /// reembolso en nombre del operador.
    /// </summary>
    public int ResolvePercentage(int daysBeforeStart)
    {
        foreach (var tier in Tiers.OrderByDescending(t => t.MinDaysBefore))
            if (daysBeforeStart >= tier.MinDaysBefore) return tier.RefundPercentage;

        return 0;
    }

    /// <summary>
    /// Errores de la política, en el idioma del operador. Vacío significa que se puede guardar.
    ///
    /// Dos reglas que no son cosméticas: los tramos no pueden repetir el mismo mínimo (dos reglas para el
    /// mismo día es una contradicción, no una preferencia) y el porcentaje no puede **subir** al acercarse
    /// la salida — eso siempre es un error de carga, nunca una política comercial real.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (Tiers.Count > MaxTiers)
            errors.Add($"No se pueden configurar más de {MaxTiers} tramos.");

        foreach (var tier in Tiers)
        {
            if (tier.MinDaysBefore is < 0 or > MaxDaysBefore)
                errors.Add($"Los días de anticipación tienen que estar entre 0 y {MaxDaysBefore}.");

            if (tier.RefundPercentage is < 0 or > 100)
                errors.Add("El porcentaje de reembolso tiene que estar entre 0 y 100.");
        }

        var ordered = Tiers.OrderByDescending(t => t.MinDaysBefore).ToList();

        if (ordered.Select(t => t.MinDaysBefore).Distinct().Count() != ordered.Count)
            errors.Add("Hay dos tramos para la misma cantidad de días: cada tramo tiene que empezar en un día distinto.");

        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].RefundPercentage > ordered[i - 1].RefundPercentage)
            {
                errors.Add(
                    "Un tramo más cercano a la salida no puede devolver más que uno más lejano: " +
                    $"con {ordered[i].MinDaysBefore} día(s) de anticipación se devolvería {ordered[i].RefundPercentage}% " +
                    $"y con {ordered[i - 1].MinDaysBefore} día(s) sólo {ordered[i - 1].RefundPercentage}%.");
                break;
            }
        }

        return errors;
    }

    /// <summary>Los tramos de mayor a menor anticipación, que es como se leen y como se muestran.</summary>
    public IReadOnlyList<CancellationTier> Ordered() => [.. Tiers.OrderByDescending(t => t.MinDaysBefore)];
}

/// <summary>Un tramo: "con al menos N días de anticipación se devuelve P%".</summary>
public sealed record CancellationTier(int MinDaysBefore, int RefundPercentage);
