namespace TurisClick.Api.Modules.Reservations.Services;

/// <summary>
/// UC-SYS-08 — expiración de holds. Toda la lógica de dominio vive acá, no en el timer: el
/// <c>ReservationExpirationBackgroundService</c> solo llama a <see cref="ExpireDueReservationsAsync"/>
/// cada tanto, y los tests invocan el servicio directamente sin depender de relojes reales.
/// </summary>
public interface IReservationExpirationService
{
    /// <summary>Expira hasta <paramref name="batchSize"/> reservas vencidas. Devuelve cuántas expiró realmente.</summary>
    Task<int> ExpireDueReservationsAsync(int batchSize, CancellationToken ct);

    /// <summary>
    /// Expira una reserva concreta si corresponde. Idempotente: devuelve false (sin liberar nada) si
    /// ya no estaba PENDING_PAYMENT, incluso ejecutándose en paralelo consigo misma.
    /// </summary>
    Task<bool> ExpireAsync(Guid reservationId, CancellationToken ct);
}
