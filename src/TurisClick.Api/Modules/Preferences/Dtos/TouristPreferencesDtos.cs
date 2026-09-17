using TurisClick.Api.Modules.Categories.Dtos;

namespace TurisClick.Api.Modules.Preferences.Dtos;

public class TouristPreferencesResponse
{
    public List<CategoryResponse> Categories { get; set; } = [];

    /// <summary>RELAXED, BALANCED, INTENSE o null.</summary>
    public string? TravelPace { get; set; }

    /// <summary>SOLO, COUPLE, FRIENDS, FAMILY o null.</summary>
    public string? TravelParty { get; set; }

    /// <summary>ECONOMY, MODERATE, PREMIUM o null.</summary>
    public string? BudgetLevel { get; set; }

    /// <summary>false = la app debería ofrecer el onboarding.</summary>
    public bool OnboardingCompleted { get; set; }
    public DateTimeOffset? OnboardingCompletedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Reemplazo completo del perfil. Todo es opcional: el turista puede saltear cualquier paso.</summary>
public class UpdateTouristPreferencesRequest
{
    public List<Guid> CategoryIds { get; set; } = [];
    public string? TravelPace { get; set; }
    public string? TravelParty { get; set; }
    public string? BudgetLevel { get; set; }

    /// <summary>true al terminar (o saltear) el onboarding. Una vez completado no vuelve a false.</summary>
    public bool CompleteOnboarding { get; set; }
}
