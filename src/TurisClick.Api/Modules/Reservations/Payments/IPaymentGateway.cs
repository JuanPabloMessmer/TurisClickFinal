namespace TurisClick.Api.Modules.Reservations.Payments;

public record PaymentChargeRequest(Guid ReservationId, decimal Amount, string Currency, bool SimulatedSuccess);

public record PaymentChargeResult(bool Approved, string? FailureReason);

/// <summary>
/// Punto de extensión para UC-T-19/UC-SYS-07. ReservationService solo conoce esta interfaz — nunca la
/// implementación concreta — para que integrar una pasarela real más adelante sea agregar una nueva
/// implementación de <see cref="ChargeAsync"/> y cambiar el registro en ReservationsModuleExtensions,
/// sin tocar la lógica transaccional de Reservations.
/// </summary>
public interface IPaymentGateway
{
    Task<PaymentChargeResult> ChargeAsync(PaymentChargeRequest request, CancellationToken ct);
}
