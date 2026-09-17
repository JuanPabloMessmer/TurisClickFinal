using TurisClick.Api.Modules.Auth.Entities;
using TurisClick.Api.Modules.Categories.Entities;

namespace TurisClick.Api.Modules.Preferences.Entities;

/// <summary>
/// Perfil de viaje persistente del turista (onboarding), 1—1 con User. Distinto de las preferencias de
/// AiConversation: estas son el punto de partida de TODAS las conversaciones, las de la conversación
/// son lo que el turista pidió en ese viaje puntual y siempre tienen prioridad.
/// Solo guarda lo que el asistente usa: intereses, ritmo, con quién viaja y nivel de gasto.
/// </summary>
public class TouristPreference
{
    /// <summary>PK y FK a users.id: un perfil por turista.</summary>
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public TravelPace? TravelPace { get; set; }
    public TravelParty? TravelParty { get; set; }
    public BudgetLevel? BudgetLevel { get; set; }

    /// <summary>Nulo mientras no terminó (o salteó explícitamente) el onboarding.</summary>
    public DateTimeOffset? OnboardingCompletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Intereses: categorías reales del catálogo, no texto libre.</summary>
    public ICollection<Category> Categories { get; set; } = new List<Category>();
}

/// <summary>Cuántas actividades por día tolera el turista.</summary>
public enum TravelPace
{
    RELAXED,
    BALANCED,
    INTENSE
}

/// <summary>Con quién viaja normalmente. SOLO/COUPLE permiten inferir la cantidad de viajeros.</summary>
public enum TravelParty
{
    SOLO,
    COUPLE,
    FRIENDS,
    FAMILY
}

/// <summary>Preferencia de gasto por actividad, en bolivianos (ver TouristPreferenceHints).</summary>
public enum BudgetLevel
{
    ECONOMY,
    MODERATE,
    PREMIUM
}
