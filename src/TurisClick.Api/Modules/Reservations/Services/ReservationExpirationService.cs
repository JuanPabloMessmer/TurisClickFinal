using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Ai.Entities;
using TurisClick.Api.Modules.Reservations.Entities;
using TurisClick.Api.Modules.Reservations.Repositories;

namespace TurisClick.Api.Modules.Reservations.Services;

public class ReservationExpirationService(
    IReservationRepository reservationRepository,
    IReservationBookingService bookingService,
    ILogger<ReservationExpirationService> logger,
    TurisClickDbContext db) : IReservationExpirationService
{
    public async Task<int> ExpireDueReservationsAsync(int batchSize, CancellationToken ct)
    {
        // Se leen candidatas sin lock a propósito: quien decide de verdad es la transición condicional
        // dentro de ExpireAsync. Si otra instancia del backend expira una entre este SELECT y el UPDATE,
        // acá simplemente se cuenta como "no expirada por mí" y no pasa nada.
        var candidates = await reservationRepository.ListExpiredCandidateIdsAsync(DateTimeOffset.UtcNow, batchSize, ct);
        if (candidates.Count == 0) return 0;

        var expired = 0;
        foreach (var reservationId in candidates)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // Una transacción POR RESERVA, no por batch: que una falle no puede impedir expirar
                // las demás ni dejar a medias las que ya se procesaron.
                if (await ExpireAsync(reservationId, ct)) expired++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "No se pudo expirar la reserva {ReservationId}; se continúa con el resto del lote.", reservationId);
            }
        }

        if (expired > 0)
            logger.LogInformation("Expiración automática: {Expired} de {Candidates} reserva(s) vencida(s).", expired, candidates.Count);

        return expired;
    }

    /// <summary>Virtual solo para que los tests puedan observar el orquestador de lotes por separado.</summary>
    public virtual async Task<bool> ExpireAsync(Guid reservationId, CancellationToken ct)
    {
        await using var tx = await BeginTransactionAsync(ct);

        // ---- La transición de estado es la ÚNICA autoridad sobre quién libera el cupo ----
        // Si el pago la confirmó, el turista la canceló, u otra instancia ya la expiró, acá se ven 0
        // filas afectadas y esta ejecución no libera nada. Eso es lo que hace la operación idempotente
        // y lo que resuelve la carrera contra el pago: solo una transición puede ganar.
        var now = DateTimeOffset.UtcNow;
        var won = await db.Reservations
            .Where(r => r.Id == reservationId
                && r.Status == ReservationStatus.PENDING_PAYMENT
                && r.ExpiresAt != null && r.ExpiresAt < now)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReservationStatus.EXPIRED), ct);

        if (won != 1)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        // A partir de acá esta transacción es la dueña indiscutida de la expiración de esta reserva.
        var items = await db.ReservationItems
            .AsNoTracking()
            .Where(i => i.ReservationId == reservationId && i.Status == ReservationItemStatus.PENDING_PAYMENT)
            .ToListAsync(ct);

        await bookingService.ReleaseHoldsAsync(items, ct);

        // EXPIRED y no CANCELLED: vencer sin pagar y decidir cancelar son eventos distintos (decisión
        // de dominio de Oleada 8). Solo se tocan las líneas que seguían activas — una que el proveedor
        // ya había cancelado conserva su estado y su motivo.
        await db.ReservationItems
            .Where(i => i.ReservationId == reservationId && i.Status == ReservationItemStatus.PENDING_PAYMENT)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, ReservationItemStatus.EXPIRED)
                .SetProperty(i => i.CancelledAt, now), ct);

        var releasedItinerary = await ReleaseAiItineraryAsync(reservationId, now, ct);

        await tx.CommitAsync(ct);

        logger.LogInformation(
            "Reserva {ReservationId} expirada: {Items} línea(s) liberada(s){Itinerary}.",
            reservationId, items.Count, releasedItinerary ? ", itinerario IA devuelto a SAVED" : string.Empty);

        return true;
    }

    /// <summary>
    /// Si la reserva venía de un itinerario IA (UC-T-18), el itinerario vuelve a SAVED para que el
    /// turista pueda reintentar — pasando otra vez por toda la revalidación final, nunca reutilizando
    /// precios ni disponibilidad viejos. Va dentro de la MISMA transacción que la liberación de cupo:
    /// un itinerario reservable cuya reserva todavía retiene cupo sería un estado contradictorio.
    /// </summary>
    private async Task<bool> ReleaseAiItineraryAsync(Guid reservationId, DateTimeOffset now, CancellationToken ct)
    {
        var itineraryId = await db.Reservations
            .AsNoTracking()
            .Where(r => r.Id == reservationId)
            .Select(r => r.AiItineraryId)
            .FirstOrDefaultAsync(ct);

        if (itineraryId is not { } id) return false;

        var updated = await db.Set<AiItinerary>()
            .Where(i => i.Id == id && i.Status == AiItineraryStatus.BOOKED)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, AiItineraryStatus.SAVED)
                .SetProperty(i => i.UpdatedAt, now), ct);

        return updated == 1;
    }

    /// <summary>Virtual solo para poder testear el orquestador de lotes sin base real; en producción es la transacción de EF/Npgsql.</summary>
    protected virtual Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) =>
        db.Database.BeginTransactionAsync(ct);
}
