namespace TurisClick.Api.Modules.Ai.Entities;

/// <summary>
/// DRAFT es el único estado que produce Oleada 5. SAVED (UC-T-16), BOOKED (UC-T-18) y DISCARDED quedan
/// definidos acá porque son parte del enum aprobado en docs/domain-model.md, pero ninguna transición
/// hacia ellos se implementa todavía (Oleada 6/7).
/// </summary>
public enum AiItineraryStatus
{
    DRAFT,
    SAVED,
    BOOKED,
    DISCARDED
}
