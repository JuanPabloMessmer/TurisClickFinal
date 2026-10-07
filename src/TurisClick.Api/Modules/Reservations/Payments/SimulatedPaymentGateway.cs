namespace TurisClick.Api.Modules.Reservations.Payments;

/// <summary>
/// Placeholder hasta integrar una pasarela real (fuera de alcance actual — decisión 4 de use-cases.md).
/// El resultado lo decide el propio caller vía <see cref="PaymentChargeRequest.SimulatedSuccess"/> para
/// poder ejercitar ambos caminos (aprobado/rechazado) en tests — una pasarela real nunca recibiría ese
/// campo del cliente, lo determinaría su propia respuesta.
/// </summary>
public class SimulatedPaymentGateway(ILogger<SimulatedPaymentGateway> logger) : IPaymentGateway
{
    public Task<PaymentChargeResult> ChargeAsync(PaymentChargeRequest request, CancellationToken ct) =>
        Task.FromResult(request.SimulatedSuccess
            ? new PaymentChargeResult(Approved: true, FailureReason: null)
            : new PaymentChargeResult(Approved: false, FailureReason: "Pago simulado rechazado."));

    /// <summary>
    /// No hay nada que revertir porque nunca se movió dinero, y eso se dice en el log en vez de disimularlo:
    /// un reverso que no existe no debería parecer un reverso exitoso. Cuando haya una pasarela real, este
    /// es el único lugar que cambia.
    /// </summary>
    public Task VoidAsync(PaymentVoidRequest request, CancellationToken ct)
    {
        logger.LogInformation(
            "Reverso del pago simulado de la reserva {ReservationId} por {Amount} {Currency}: {Reason}. " +
            "No se movió dinero porque el cobro era simulado.",
            request.ReservationId, request.Amount, request.Currency, request.Reason);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Con el cobro simulado no hay plata que devolver, así que esto siempre "funciona" — y el log lo dice
    /// con esas palabras en vez de aparentar un reembolso que no existió. Lo que sí es real es el asiento en
    /// el libro: el día que haya una pasarela de verdad, lo único que cambia es esta clase.
    ///
    /// No se registra ni el importe junto a datos de la persona ni ninguna referencia de medio de pago: el
    /// identificador que devuelve es nuestro.
    /// </summary>
    public Task<PaymentRefundResult> RefundAsync(PaymentRefundRequest request, CancellationToken ct)
    {
        logger.LogInformation(
            "Reembolso simulado de la reserva {ReservationId}: {Amount} {Currency} ({Reason}). " +
            "No se movió dinero porque el cobro era simulado.",
            request.ReservationId, request.Amount, request.Currency, request.Reason);

        return Task.FromResult(new PaymentRefundResult(
            Succeeded: true, FailureReason: null, Reference: $"sim_{request.IdempotencyKey}"));
    }
}
