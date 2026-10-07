namespace TurisClick.Api.Modules.Reservations.Entities;

/// <summary>
/// Un movimiento de dinero de una reserva. La tabla es **un libro, no un estado**: las filas se agregan y
/// no se modifican nunca.
///
/// Por qué importa la diferencia: si un cobro de USD 1.510 se "convirtiera" en un reembolso de USD 1.000,
/// se perdería que alguna vez se cobraron 1.510, y con eso la posibilidad de responder cuánto se cobró,
/// cuánto se devolvió, qué queda efectivamente pagado y qué intento falló. Guardando los dos eventos no se
/// pierde nada:
///
/// <code>
///   CHARGE   SUCCEEDED   1510.00 USD
///   REFUND   SUCCEEDED    545.00 USD   (paquete)
///   REFUND   FAILED       380.00 USD   (vuelo)   ← el intento fallido también es historia
/// </code>
///
/// No es software contable: no hay asientos dobles, ni conciliación bancaria, ni cierres. Es el mínimo que
/// permite explicar qué pasó con la plata de una reserva.
///
/// **Nunca guarda datos de medio de pago**: ni número de tarjeta, ni CVV, ni token. Lo que puede guardar es
/// la referencia que devuelve la pasarela, que es un identificador opaco suyo.
/// </summary>
public class PaymentTransaction
{
    public Guid Id { get; set; }

    public Guid ReservationId { get; set; }
    public Reservation? Reservation { get; set; }

    public PaymentTransactionType Type { get; set; }
    public PaymentTransactionStatus Status { get; set; }

    /// <summary>Siempre positivo. El signo lo da <see cref="Type"/>: sumar cargos y restar reembolsos es del lector, no del dato.</summary>
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Qué componente de la reserva originó el movimiento. Un cobro agrupa toda la reserva en una moneda
    /// —es una sola operación de pago— y queda nulo; un reembolso siempre nace de un componente concreto,
    /// porque la política del paquete y la de la aerolínea son eventos financieros distintos.
    /// </summary>
    public PaymentComponent? Component { get; set; }

    /// <summary>Línea de la reserva que originó el movimiento, cuando corresponde a una.</summary>
    public Guid? ReservationItemId { get; set; }

    /// <summary>Quién movió la plata ("Simulated" hoy; el nombre de la pasarela real cuando exista).</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Identificador del movimiento en la pasarela, si lo devuelve. Opaco.</summary>
    public string? ProviderReference { get; set; }

    /// <summary>
    /// Clave propia del movimiento. En los reembolsos es **única**: un reembolso repetido por un reintento
    /// choca contra el índice en vez de devolver la plata dos veces. Los cobros comparten la clave de la
    /// reserva porque reintentar un pago rechazado es legítimo y cada intento es una fila.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Por qué falló, cuando falló. Nunca lleva datos de la persona ni de su medio de pago.</summary>
    public string? FailureReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public enum PaymentTransactionType
{
    /// <summary>Cobro al turista.</summary>
    CHARGE,

    /// <summary>Reverso de un cobro que todavía no se había capturado.</summary>
    VOID,

    /// <summary>Devolución de dinero ya cobrado.</summary>
    REFUND,
}

public enum PaymentTransactionStatus
{
    PENDING,
    SUCCEEDED,
    FAILED,
}

/// <summary>De dónde sale el dinero de un movimiento. No se mezcla un paquete con un pasaje aéreo.</summary>
public enum PaymentComponent
{
    PACKAGE,
    EXPERIENCE,
    FLIGHT,
}
