namespace TurisClick.Api.Modules.Reservations.Payments;

public record PaymentChargeRequest(Guid ReservationId, decimal Amount, string Currency, bool SimulatedSuccess);

public record PaymentChargeResult(bool Approved, string? FailureReason);

/// <summary>
/// Deshacer un cobro ya aprobado. Existe porque la reserva coordinada lo necesita de verdad: si el pago se
/// autoriza y después la emisión del vuelo falla de forma definitiva, hay que revertirlo, y ese punto tiene
/// que estar en el código y no en un comentario que diga "acá habría que devolver la plata".
/// </summary>
public record PaymentVoidRequest(Guid ReservationId, decimal Amount, string Currency, string Reason);

/// <summary>
/// Devolución de dinero ya cobrado. Lleva el componente del que sale porque el reembolso de un paquete y el
/// de un pasaje aéreo son eventos financieros distintos, con políticas distintas y montos distintos.
///
/// `IdempotencyKey` la calcula el servidor a partir de la cancelación y del componente: dos intentos del
/// mismo reembolso traen la misma clave, y el libro la rechaza por índice único.
/// </summary>
public record PaymentRefundRequest(
    Guid ReservationId,
    decimal Amount,
    string Currency,
    string Reason,
    string IdempotencyKey);

public record PaymentRefundResult(bool Succeeded, string? FailureReason, string? Reference);

/// <summary>
/// Punto de extensión para UC-T-19/UC-SYS-07. ReservationService solo conoce esta interfaz — nunca la
/// implementación concreta — para que integrar una pasarela real más adelante sea agregar una nueva
/// implementación de <see cref="ChargeAsync"/> y cambiar el registro en ReservationsModuleExtensions,
/// sin tocar la lógica transaccional de Reservations.
/// </summary>
public interface IPaymentGateway
{
    Task<PaymentChargeResult> ChargeAsync(PaymentChargeRequest request, CancellationToken ct);

    /// <summary>
    /// Revierte un cobro aprobado. Con la pasarela simulada no hay dinero que mover y no puede fallar; con
    /// una real sería el void/refund de una autorización, y el orden del checkout
    /// (autorizar → emitir → capturar) está pensado para que este caso sea raro y acotado.
    /// </summary>
    Task VoidAsync(PaymentVoidRequest request, CancellationToken ct);

    /// <summary>
    /// Devuelve dinero ya cobrado. Puede fallar —y el flujo de cancelación contempla que falle—: con una
    /// pasarela real un reembolso se rechaza por un medio de pago vencido, por un límite o porque el
    /// proveedor está caído, y en ninguno de esos casos corresponde deshacer la cancelación ya ejecutada.
    /// </summary>
    Task<PaymentRefundResult> RefundAsync(PaymentRefundRequest request, CancellationToken ct);
}
