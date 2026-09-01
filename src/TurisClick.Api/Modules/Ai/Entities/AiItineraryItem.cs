using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Packages.Entities;
using TurisClick.Api.Modules.Reservations.Entities;

namespace TurisClick.Api.Modules.Ai.Entities;

/// <summary>
/// docs/domain-model.md §8. Mismo rol que ReservationItem (una línea que referencia un producto real)
/// pero antes de convertirse en reserva (UC-T-18, Oleada 7) — nunca contiene datos inventados, siempre
/// referencia una fila real de Experience o Package ya validada por el backend (nunca ciegamente lo que
/// devolvió el LLM).
/// </summary>
public class AiItineraryItem
{
    public Guid Id { get; set; }

    public Guid AiItineraryId { get; set; }
    public AiItinerary? AiItinerary { get; set; }

    public int DayNumber { get; set; }
    public int SortOrder { get; set; }

    public ProductType ProductType { get; set; }

    public Guid? ExperienceId { get; set; }
    public Experience? Experience { get; set; }

    public Guid? PackageId { get; set; }
    public Package? Package { get; set; }

    /// <summary>
    /// Nulo mientras la propuesta todavía razona en términos de "Día N" sin fecha calendario definitiva
    /// (a diferencia de ReservationItem, acá SÍ puede quedar sin resolver incluso para EXPERIENCE).
    /// </summary>
    public Guid? ExperienceAvailabilityId { get; set; }
    public ExperienceAvailability? ExperienceAvailability { get; set; }

    public Guid? PackageAvailabilityId { get; set; }
    public PackageAvailability? PackageAvailability { get; set; }

    /// <summary>Precio recuperado en el momento de la propuesta (UC-AI-02) — se vuelve a validar recién antes de reservar (UC-T-18, Oleada 7).</summary>
    public decimal EstimatedUnitPrice { get; set; }
    public string Currency { get; set; } = string.Empty;
}
