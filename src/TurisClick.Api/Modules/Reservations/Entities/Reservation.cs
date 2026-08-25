using TurisClick.Api.Modules.Auth.Entities;

namespace TurisClick.Api.Modules.Reservations.Entities;

/// <summary>
/// docs/domain-model.md §7 — modelo unificado: la misma entidad sirve tanto para una compra directa
/// (un solo Item) como, más adelante, para un itinerario IA reservado (varios Items, posiblemente de
/// distintas empresas). No persiste TotalPrice/Currency (puede haber múltiples monedas entre sus
/// Items) ni un campo Type (se deriva de AiItineraryId + Items.ProductType) — ver docs/domain-model.md.
/// </summary>
public class Reservation
{
    public Guid Id { get; set; }

    public Guid TouristId { get; set; }
    public User? Tourist { get; set; }

    /// <summary>
    /// FK lógica a ai_itineraries.id. Sin restricción física todavía: esa tabla no existe hasta la
    /// Oleada 5+ (mismo patrón que company_id en Oleada 0 — ver database-design.md).
    /// </summary>
    public Guid? AiItineraryId { get; set; }

    public ReservationStatus Status { get; set; } = ReservationStatus.PENDING_PAYMENT;

    /// <summary>Límite del hold de cupo mientras está PENDING_PAYMENT. La liberación efectiva es UC-SYS-08 (Oleada 8).</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    public ICollection<ReservationItem> Items { get; set; } = new List<ReservationItem>();
}
