using TurisClick.Api.Modules.Preferences.Entities;

namespace TurisClick.Api.Modules.Preferences.Services;

/// <summary>
/// Traducción del perfil a señales que el retrieval entiende. El nivel de gasto se expresa como precio
/// máximo POR ACTIVIDAD en bolivianos, la moneda del catálogo: no se inventa conversión a otras monedas
/// (un producto en otra moneda simplemente no suma ni resta por presupuesto).
/// </summary>
public static class TouristPreferenceHints
{
    public const string BudgetCurrency = "BOB";

    public static decimal? MaxPricePerActivity(BudgetLevel level) => level switch
    {
        BudgetLevel.ECONOMY => 250m,
        BudgetLevel.MODERATE => 600m,
        _ => null
    };

    /// <summary>Actividades por día que el compositor determinístico propone según el ritmo.</summary>
    public static int ActivitiesPerDay(string? pace) => pace?.ToUpperInvariant() switch
    {
        "INTENSE" => 2,
        _ => 1
    };
}
