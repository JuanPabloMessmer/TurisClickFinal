namespace TurisClick.Api.Modules.Reservations.Entities;

/// <summary>
/// Una cancelación de reserva: primero el presupuesto que se le muestra a la persona, después el registro
/// de lo que efectivamente pasó. Son la misma fila a propósito —lo que se acepta y lo que se ejecuta tienen
/// que ser lo mismo—, y es lo que impide que el cliente mande importes de reembolso.
///
/// Se persiste porque la aceptación tiene que atarse a **este** cálculo: el precio del paquete lo decide
/// una política que ya está congelada, pero el reembolso del vuelo lo informa la aerolínea y puede cambiar
/// o vencer. Sin una fila que guarde lo que se mostró, "acepto" no significa nada verificable.
/// </summary>
public class ReservationCancellation
{
    public Guid Id { get; set; }

    public Guid ReservationId { get; set; }
    public Reservation? Reservation { get; set; }

    public ReservationCancellationStatus Status { get; set; } = ReservationCancellationStatus.QUOTED;

    /// <summary>
    /// Hasta cuándo vale el presupuesto. Lo manda la aerolínea cuando hay vuelo —Duffel da un `expires_at`
    /// y pasado ese momento su cancelación ya no se puede confirmar—; sin vuelo es nuestra propia ventana,
    /// corta, para que nadie acepte un cálculo de ayer.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Identificador de la cancelación pendiente en el proveedor aéreo. Duffel sólo permite confirmar la
    /// **última** cancelación creada para una orden, así que pedir un presupuesto nuevo invalida el
    /// anterior: por eso se guarda junto al presupuesto y no aparte.
    /// </summary>
    public string? ProviderCancellationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Cuándo la persona aceptó este presupuesto. A partir de acá la cancelación es irreversible.</summary>
    public DateTimeOffset? AcceptedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Cuándo quedó cancelado el vuelo en la aerolínea. Nulo si no había vuelo o si todavía no se hizo.</summary>
    public DateTimeOffset? FlightCancelledAt { get; set; }

    /// <summary>Qué salió mal, cuando algo salió mal. Sin datos de la persona ni de su medio de pago.</summary>
    public string? FailureReason { get; set; }

    /// <summary>Intentos de resolver un reembolso pendiente. Acotados: no se insiste para siempre.</summary>
    public int ResolutionAttempts { get; set; }

    public DateTimeOffset? NextResolutionAt { get; set; }

    public ICollection<ReservationCancellationLine> Lines { get; set; } = new List<ReservationCancellationLine>();
}

/// <summary>
/// Estados de una cancelación.
///
/// <code>
///   QUOTED ──(acepta)──► ACCEPTED ──┬──► COMPLETED          todo cancelado y reembolsado
///      │                            ├──► REFUND_PENDING     cancelado; el reembolso quedó por reintentar
///      │                            ├──► REQUIRES_REVIEW    quedó a medias y lo resuelve una persona
///      │                            └──► FAILED             no se canceló nada; la reserva sigue viva
///      └──(vence)──────► EXPIRED
/// </code>
/// </summary>
public enum ReservationCancellationStatus
{
    /// <summary>Presupuestada y mostrada. Todavía no se canceló nada.</summary>
    QUOTED,

    /// <summary>Aceptada: se escribe antes de tocar la aerolínea, para que una caída deje rastro.</summary>
    ACCEPTED,

    COMPLETED,

    /// <summary>Lo cancelado está cancelado, pero algún reembolso no se pudo registrar todavía.</summary>
    REFUND_PENDING,

    /// <summary>Quedó a medias de una forma que el sistema no puede resolver solo. No se disimula.</summary>
    REQUIRES_REVIEW,

    /// <summary>No se canceló nada. La reserva vuelve a estar vigente.</summary>
    FAILED,

    EXPIRED,
}

/// <summary>
/// Un componente de la cancelación, con su plata. Existe una línea por producto y, si hay vuelo, una línea
/// aérea aparte: el reembolso del paquete lo decide la política del operador y el del pasaje lo decide la
/// aerolínea, y mezclarlos sería aplicarle a una la regla de la otra.
/// </summary>
public class ReservationCancellationLine
{
    public Guid Id { get; set; }

    public Guid CancellationId { get; set; }
    public ReservationCancellation? Cancellation { get; set; }

    public PaymentComponent Component { get; set; }

    /// <summary>Línea de la reserva de la que sale, cuando corresponde a una (nulo en la del vuelo).</summary>
    public Guid? ReservationItemId { get; set; }

    /// <summary>Qué se mostrará como título: el nombre del producto o la ruta del vuelo.</summary>
    public string Label { get; set; } = string.Empty;

    public decimal PaidAmount { get; set; }
    public decimal RefundAmount { get; set; }

    /// <summary>Lo que se queda el proveedor: paga - reembolso. Se guarda explícito para no hacérselo calcular a nadie.</summary>
    public decimal FeeAmount { get; set; }

    public string Currency { get; set; } = string.Empty;

    /// <summary>La política que se aplicó, tal como estaba congelada en la reserva. Nulo en la línea del vuelo.</summary>
    public string? PolicyApplied { get; set; }

    /// <summary>El porcentaje que resultó de esa política. Nulo en la línea del vuelo.</summary>
    public int? RefundPercentage { get; set; }

    /// <summary>
    /// false cuando el proveedor **no informó** cuánto devuelve. No es lo mismo que devolver cero: una cosa
    /// es "no hay reembolso" y otra es "no lo sabemos", y decirle a alguien que su pasaje es reembolsable
    /// sin que la aerolínea lo haya confirmado es prometer plata ajena.
    /// </summary>
    public bool RefundKnown { get; set; } = true;

    /// <summary>Explicación para la persona: por qué se devuelve eso y no otra cosa.</summary>
    public string Explanation { get; set; } = string.Empty;
}
