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
}
