namespace TurisClick.Api.Modules.Reservations.Payments;

/// <summary>
/// Placeholder hasta integrar una pasarela real (fuera de alcance actual — decisión 4 de use-cases.md).
/// El resultado lo decide el propio caller vía <see cref="PaymentChargeRequest.SimulatedSuccess"/> para
/// poder ejercitar ambos caminos (aprobado/rechazado) en tests — una pasarela real nunca recibiría ese
/// campo del cliente, lo determinaría su propia respuesta.
/// </summary>
public class SimulatedPaymentGateway : IPaymentGateway
{
    public Task<PaymentChargeResult> ChargeAsync(PaymentChargeRequest request, CancellationToken ct) =>
        Task.FromResult(request.SimulatedSuccess
            ? new PaymentChargeResult(Approved: true, FailureReason: null)
            : new PaymentChargeResult(Approved: false, FailureReason: "Pago simulado rechazado."));
}
