using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Reservations.Dtos;

/// <summary>
/// Un tramo de la política de cancelación, como lo configura y lo lee el operador.
/// </summary>
public class CancellationTierDto
{
    /// <summary>Días de anticipación a partir de los cuales aplica este tramo.</summary>
    [Range(0, 365)]
    public int MinDaysBefore { get; set; }

    [Range(0, 100)]
    public int RefundPercentage { get; set; }
}

/// <summary>
/// Lo que el turista ve ANTES de cancelar: qué se cancela, cuánto se devuelve y por qué.
///
/// El cliente nunca manda importes: manda el id de este presupuesto. Todos los números de acá los calculó el
/// servidor —la parte del paquete con la política congelada en la reserva, la del vuelo con lo que informó
/// la aerolínea— y confirmar significa aceptar exactamente estos.
/// </summary>
public class CancellationQuoteResponse
{
    public Guid QuoteId { get; set; }
    public Guid ReservationId { get; set; }

    /// <summary>Hasta cuándo vale. Si vence hay que volver a pedirlo: el reembolso aéreo puede cambiar.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    public List<CancellationLineResponse> Lines { get; set; } = [];

    /// <summary>Reembolso total **por moneda**. Nunca un único número si las monedas difieren.</summary>
    public List<MoneyLineResponse> Refunds { get; set; } = [];

    /// <summary>Lo que se queda cada proveedor, por moneda.</summary>
    public List<MoneyLineResponse> Fees { get; set; } = [];

    /// <summary>true si alguna línea tiene reembolso desconocido: la app no promete un total exacto.</summary>
    public bool HasUnknownRefund { get; set; }

    /// <summary>Resumen en una frase, decidido por el dominio y no por la pantalla.</summary>
    public string Summary { get; set; } = string.Empty;
}

public class CancellationLineResponse
{
    /// <summary>PACKAGE | EXPERIENCE | FLIGHT</summary>
    public string Component { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public decimal PaidAmount { get; set; }
    public decimal RefundAmount { get; set; }
    public decimal FeeAmount { get; set; }
    public string Currency { get; set; } = string.Empty;

    /// <summary>Porcentaje que resultó de la política del operador. Nulo en la línea del vuelo.</summary>
    public int? RefundPercentage { get; set; }

    /// <summary>false cuando el proveedor no informó cuánto devuelve. Distinto de devolver cero.</summary>
    public bool RefundKnown { get; set; }

    public string Explanation { get; set; } = string.Empty;
}

public class MoneyLineResponse
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

/// <summary>
/// Confirmación de la cancelación. Sólo el id del presupuesto: ningún importe viaja desde el cliente.
/// </summary>
public class ConfirmCancellationRequest
{
    /// <summary>
    /// Presupuesto que la persona aceptó. Obligatorio para una reserva ya confirmada; una reserva que
    /// todavía no se pagó se cancela sin presupuesto, porque no hay plata que devolver.
    /// </summary>
    public Guid? CancellationQuoteId { get; set; }
}

/// <summary>Resultado de ejecutar la cancelación, con el detalle de lo que efectivamente pasó.</summary>
public class CancellationResultResponse
{
    public Guid ReservationId { get; set; }

    /// <summary>COMPLETED | REFUND_PENDING | REQUIRES_REVIEW | FAILED</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Qué decirle a la persona. Nunca anuncia un éxito que el backend no confirmó.</summary>
    public string Message { get; set; } = string.Empty;

    public List<CancellationLineResponse> Lines { get; set; } = [];

    /// <summary>Lo efectivamente reembolsado, por moneda.</summary>
    public List<MoneyLineResponse> Refunds { get; set; } = [];

    /// <summary>true si el vuelo quedó cancelado en la aerolínea.</summary>
    public bool FlightCancelled { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>Vista del libro de pagos de una reserva. Para el ADMIN: operación, no contabilidad.</summary>
public class PaymentTransactionResponse
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? Component { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ReservationPaymentsResponse
{
    public Guid ReservationId { get; set; }
    public string ReservationStatus { get; set; } = string.Empty;

    /// <summary>Cobrado, devuelto y neto por moneda. Nunca un total único entre monedas distintas.</summary>
    public List<PaymentBalanceResponse> Balances { get; set; } = [];

    /// <summary>El libro completo, en orden. Incluye los intentos fallidos: también son historia.</summary>
    public List<PaymentTransactionResponse> Transactions { get; set; } = [];

    /// <summary>Las cancelaciones de esta reserva, con su estado operativo.</summary>
    public List<CancellationSummaryResponse> Cancellations { get; set; } = [];
}

public class PaymentBalanceResponse
{
    public string Currency { get; set; } = string.Empty;
    public decimal Charged { get; set; }
    public decimal Refunded { get; set; }
    public decimal Net { get; set; }
}

public class CancellationSummaryResponse
{
    public Guid Id { get; set; }
    public Guid ReservationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public bool FlightCancelled { get; set; }
    public string? FailureReason { get; set; }
    public int ResolutionAttempts { get; set; }
    public List<CancellationLineResponse> Lines { get; set; } = [];
}

/// <summary>
/// Resultado de reintentar la cola de cancelaciones pendientes. Se devuelven los tres números porque
/// "completé 2" sin saber cuántas había ni cuántas quedan no le dice nada a quien está operando.
/// </summary>
public class ResolvePendingCancellationsResponse
{
    /// <summary>Cuántas estaban esperando resolución cuando se pidió el reintento.</summary>
    public int Attempted { get; set; }

    /// <summary>Cuántas quedaron completadas en esta pasada.</summary>
    public int Completed { get; set; }

    /// <summary>Cuántas siguen pendientes. Si no baja, el problema no es el proceso de fondo.</summary>
    public int StillPending { get; set; }
}
