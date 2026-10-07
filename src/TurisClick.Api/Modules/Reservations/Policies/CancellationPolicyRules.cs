using TurisClick.Api.Modules.Reservations.Dtos;

namespace TurisClick.Api.Modules.Reservations.Policies;

/// <summary>
/// Puente entre lo que manda el operador por la API y el objeto de valor. Vive acá, y no en cada DTO, para
/// que el paquete y cualquier producto que mañana tenga política validen y serialicen con las mismas reglas:
/// una política que se valida distinto según por dónde entró no es una política.
/// </summary>
public static class CancellationPolicyRules
{
    /// <summary>Errores en el idioma del operador. Vacío significa que se puede guardar.</summary>
    public static IReadOnlyList<string> Validate(IReadOnlyList<CancellationTierDto>? tiers)
    {
        // Sin política es una opción válida y significa "no se cancela desde la app". No es un error.
        if (tiers is null || tiers.Count == 0) return [];

        return ToPolicy(tiers)!.Validate();
    }

    public static CancellationPolicy? ToPolicy(IReadOnlyList<CancellationTierDto>? tiers) =>
        tiers is null || tiers.Count == 0
            ? null
            : new CancellationPolicy([.. tiers.Select(t => new CancellationTier(t.MinDaysBefore, t.RefundPercentage))]);

    /// <summary>La forma serializada que va a la base, o null si el operador no definió política.</summary>
    public static string? Serialize(IReadOnlyList<CancellationTierDto>? tiers) => ToPolicy(tiers)?.Serialize();

    /// <summary>De la columna a los tramos que lee el operador, de mayor a menor anticipación.</summary>
    public static List<CancellationTierDto> Deserialize(string? value) =>
        CancellationPolicy.TryParse(value, out var policy)
            ? [.. policy!.Ordered().Select(t => new CancellationTierDto
            {
                MinDaysBefore = t.MinDaysBefore,
                RefundPercentage = t.RefundPercentage,
            })]
            : [];
}
