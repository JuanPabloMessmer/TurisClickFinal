namespace TurisClick.Api.Modules.Reservations.Dtos;

/// <summary>Vista del TOURIST dueño (UC-T-08/10) — incluye todos los Items, sin importar a qué empresa pertenezca cada uno.</summary>
public class ReservationResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public List<ReservationItemResponse> Items { get; set; } = [];

    /// <summary>Calculado (nunca persistido): suma de Items.Subtotal agrupada por moneda — puede haber más de un total si se mezclan monedas.</summary>
    public List<ReservationTotalResponse> Totals { get; set; } = [];

    /// <summary>
    /// UC-SYS-02 — solo se popula al llamar a Pay. true si 1+ Items cambiaron de precio y el caller no
    /// envió AcceptPriceChanges=true: no se cobró ni se confirmó nada, Status queda igual que antes de
    /// llamar. Revisar CurrentUnitPrice/CurrentCurrency de cada Item y reenviar el pago aceptando el
    /// nuevo precio. false en cualquier otra respuesta (no hubo revalidación).
    /// </summary>
    public bool RequiresPriceAcceptance { get; set; }

    /// <summary>UC-T-19 — resultado del intento de pago simulado. null salvo como resultado directo de Pay.</summary>
    public bool? PaymentApproved { get; set; }

    /// <summary>Motivo del rechazo devuelto por el gateway — solo poblado cuando PaymentApproved es false.</summary>
    public string? PaymentFailureReason { get; set; }
}

/// <summary>Vista del PROVIDER dueño de ESE Item puntual (UC-P-12/13) — no expone los Items de otras empresas de la misma Reservation.</summary>
public class ReservationItemResponse
{
    public Guid Id { get; set; }
    public Guid ReservationId { get; set; }
    public string ReservationStatus { get; set; } = string.Empty;
    public string ProductType { get; set; } = string.Empty;
    public Guid? ExperienceId { get; set; }
    public string? ExperienceTitle { get; set; }
    public Guid? PackageId { get; set; }
    public string? PackageTitle { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>Solo poblado en la vista del PROVIDER (UC-P-12/13) — irrelevante en "mis reservas" del propio TOURIST.</summary>
    public Guid TouristId { get; set; }
    public string TouristName { get; set; } = string.Empty;

    public int Travelers { get; set; }
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateOnly? Date { get; set; }
    public TimeOnly? StartTime { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// UC-SYS-02 — solo se calcula al pagar (UC-T-19): true si el precio vigente de la Experience difiere
    /// del UnitPrice congelado en la reserva. Bloqueante: si es true y el caller no envió
    /// AcceptPriceChanges=true, Pay no cobra ni confirma nada. false en cualquier otra respuesta (no hubo
    /// revalidación) o una vez que el nuevo precio ya fue aceptado y recongelado.
    /// </summary>
    public bool PriceChanged { get; set; }

    /// <summary>Precio/moneda vigentes de la Experience al momento de pagar — solo poblado cuando PriceChanged es true.</summary>
    public decimal? CurrentUnitPrice { get; set; }
    public string? CurrentCurrency { get; set; }
}

public class ReservationTotalResponse
{
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
