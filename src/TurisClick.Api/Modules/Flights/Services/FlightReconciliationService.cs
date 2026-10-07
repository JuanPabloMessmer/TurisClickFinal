using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Flights.Entities;
using TurisClick.Api.Modules.Reservations.Entities;
using TurisClick.Api.Modules.Reservations.Payments;
using TurisClick.Api.Modules.Reservations.Services;

namespace TurisClick.Api.Modules.Flights.Services;

/// <summary>Configuración del reconciliador (sección `Flights:Reconciliation`).</summary>
public class FlightReconciliationOptions
{
    public const string SectionName = "Flights:Reconciliation";

    /// <summary>Los tests lo apagan: ahí la reconciliación se invoca a mano, sin esperar un timer.</summary>
    public bool Enabled { get; set; } = true;

    public int IntervalSeconds { get; set; } = 60;

    /// <summary>Cuántas emisiones sin resolver se procesan por pasada. Son pocas por definición.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>
    /// Cuántas veces se le pregunta al proveedor antes de dar una orden por inexistente. Más de una porque
    /// una orden recién creada puede tardar en aparecer, y declarar "no existe" de más significa liberar un
    /// cupo que sí estaba vendido.
    /// </summary>
    public int MaxAttempts { get; set; } = 4;

    /// <summary>
    /// Después de cuántos minutos un ORDERING se considera abandonado. Es el caso del proceso que se cayó
    /// entre marcar el estado y recibir la respuesta: nadie va a resolverlo salvo este servicio.
    /// </summary>
    public int StuckOrderingMinutes { get; set; } = 5;
}

public enum FlightReconciliationOutcome
{
    /// <summary>La orden existía: el vuelo y la reserva quedaron confirmados.</summary>
    CONFIRMED,

    /// <summary>No existe ninguna orden y se agotaron los intentos: se liberó el cupo del paquete.</summary>
    FAILED,

    /// <summary>La orden existía pero la reserva ya no: se canceló en el proveedor para no quedarse con un pasaje huérfano.</summary>
    ORDER_CANCELLED,

    /// <summary>Todavía no se puede decidir: queda agendado otro intento.</summary>
    RETRY_SCHEDULED,

    /// <summary>Esta reserva aérea no necesitaba reconciliación (otra ejecución la resolvió primero).</summary>
    NOT_APPLICABLE,
}

public interface IFlightReconciliationService
{
    Task<int> ReconcilePendingAsync(int batchSize, CancellationToken ct);
    Task<FlightReconciliationOutcome> ReconcileAsync(Guid bookingId, CancellationToken ct);
}

/// <summary>
/// Resuelve las emisiones de desenlace desconocido: las que quedaron en RECONCILIATION_REQUIRED porque la
/// respuesta del proveedor nunca llegó, y las que quedaron en ORDERING porque el proceso se cayó en el
/// peor momento posible.
///
/// La regla que gobierna todo este servicio: **nunca volver a emitir para averiguar qué pasó**. Primero se
/// le pregunta al proveedor si la orden existe; sólo entonces se confirma o se compensa. Un reintento a
/// ciegas de `CreateOrder` es justamente lo que produce dos pasajes cobrados.
///
/// Los intentos son acotados y con espera creciente: una orden recién creada puede tardar en aparecer en
/// el listado del proveedor, así que decir "no existe" en el primer intento sería liberar un cupo que sí
/// se vendió.
/// </summary>
public class FlightReconciliationService(
    TurisClickDbContext db,
    IFlightProvider flightProvider,
    IFlightBookingOrchestrator orchestrator,
    IReservationBookingService bookingService,
    IPaymentGateway paymentGateway,
    IOptions<FlightReconciliationOptions> options,
    ILogger<FlightReconciliationService> logger) : IFlightReconciliationService
{
    private readonly FlightReconciliationOptions _options = options.Value;

    /// <summary>Espera antes del siguiente intento, por número de intento. El último valor se repite si hiciera falta.</summary>
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
    ];

    public async Task<int> ReconcilePendingAsync(int batchSize, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var stuckBefore = now.AddMinutes(-_options.StuckOrderingMinutes);

        // Se leen candidatas sin lock: quien decide es la transición condicional de cada reconciliación.
        var candidates = await db.FlightBookings
            .AsNoTracking()
            .Where(b =>
                (b.Status == FlightBookingStatus.RECONCILIATION_REQUIRED
                    && (b.NextReconciliationAt == null || b.NextReconciliationAt <= now))
                || (b.Status == FlightBookingStatus.ORDERING && b.CreatedAt < stuckBefore))
            .OrderBy(b => b.CreatedAt)
            .Take(Math.Clamp(batchSize, 1, 200))
            .Select(b => b.Id)
            .ToListAsync(ct);

        if (candidates.Count == 0) return 0;

        var resolved = 0;
        foreach (var bookingId in candidates)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var outcome = await ReconcileAsync(bookingId, ct);
                if (outcome is FlightReconciliationOutcome.CONFIRMED
                    or FlightReconciliationOutcome.FAILED
                    or FlightReconciliationOutcome.ORDER_CANCELLED) resolved++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Una que falle no puede impedir resolver las demás.
                logger.LogError(ex, "No se pudo reconciliar el vuelo {BookingId}; se sigue con el resto del lote.", bookingId);
            }
        }

        logger.LogInformation(
            "Reconciliación de vuelos: {Resolved} de {Candidates} caso(s) resuelto(s).", resolved, candidates.Count);

        return resolved;
    }

    public async Task<FlightReconciliationOutcome> ReconcileAsync(Guid bookingId, CancellationToken ct)
    {
        var booking = await db.FlightBookings
            .Include(b => b.FlightQuote)
            .Include(b => b.Reservation)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null) return FlightReconciliationOutcome.NOT_APPLICABLE;

        if (booking.Status is not (FlightBookingStatus.RECONCILIATION_REQUIRED or FlightBookingStatus.ORDERING))
            return FlightReconciliationOutcome.NOT_APPLICABLE;

        var now = DateTimeOffset.UtcNow;
        booking.LastReconciliationAt = now;
        booking.ReconciliationAttempts++;

        FlightOrderResult? order;
        try
        {
            order = await FindOrderAsync(booking, ct);
        }
        catch (FlightProviderException ex)
        {
            logger.LogWarning(ex, "El proveedor no pudo responder por el vuelo {BookingId}; se reagenda.", bookingId);
            return await ScheduleRetryAsync(booking, now, ct);
        }

        if (order is null)
        {
            if (booking.ReconciliationAttempts < _options.MaxAttempts)
            {
                logger.LogInformation(
                    "Vuelo {BookingId}: el proveedor no muestra ninguna orden (intento {Attempt} de {Max}).",
                    bookingId, booking.ReconciliationAttempts, _options.MaxAttempts);

                return await ScheduleRetryAsync(booking, now, ct);
            }

            return await CompensateAsync(booking, now, ct);
        }

        // La orden existe. Lo que decide qué hacer con ella es el estado de la reserva, no el del vuelo.
        return booking.Reservation?.Status == ReservationStatus.PENDING_PAYMENT
            ? await ConfirmAsync(booking, order, now, ct)
            : await CancelOrphanOrderAsync(booking, order, now, ct);
    }

    /// <summary>
    /// Con el id de la orden se consulta directo; sin él se busca por la oferta y por nuestra clave de
    /// correlación, que es para esto que se escribe antes de llamar.
    /// </summary>
    private async Task<FlightOrderResult?> FindOrderAsync(FlightBooking booking, CancellationToken ct)
    {
        if (booking.ProviderOrderId is { Length: > 0 } orderId)
            return await flightProvider.GetOrderAsync(orderId, ct);

        var offerId = booking.FlightQuote?.ProviderOfferId;
        if (string.IsNullOrWhiteSpace(offerId)) return null;

        return await flightProvider.FindOrderByOfferAsync(offerId, booking.IdempotencyKey, ct);
    }

    private async Task<FlightReconciliationOutcome> ScheduleRetryAsync(
        FlightBooking booking, DateTimeOffset now, CancellationToken ct)
    {
        var index = Math.Min(booking.ReconciliationAttempts - 1, Backoff.Length - 1);

        booking.Status = FlightBookingStatus.RECONCILIATION_REQUIRED;
        booking.NextReconciliationAt = now.Add(Backoff[Math.Max(index, 0)]);

        await db.SaveChangesAsync(ct);
        return FlightReconciliationOutcome.RETRY_SCHEDULED;
    }

    /// <summary>La orden existía: se confirman el vuelo y la reserva en una sola transacción local.</summary>
    private async Task<FlightReconciliationOutcome> ConfirmAsync(
        FlightBooking booking, FlightOrderResult order, DateTimeOffset now, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var won = await db.Reservations
            .Where(r => r.Id == booking.ReservationId && r.Status == ReservationStatus.PENDING_PAYMENT)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ReservationStatus.CONFIRMED)
                .SetProperty(r => r.ConfirmedAt, now), ct);

        if (won != 1)
        {
            // Otra transición ganó entre la lectura y este punto. No se confirma nada y se vuelve a mirar
            // en la próxima pasada, que ya verá el estado nuevo.
            await tx.RollbackAsync(ct);
            return FlightReconciliationOutcome.RETRY_SCHEDULED;
        }

        await db.ReservationItems
            .Where(i => i.ReservationId == booking.ReservationId && i.Status == ReservationItemStatus.PENDING_PAYMENT)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, ReservationItemStatus.CONFIRMED), ct);

        orchestrator.Apply(
            booking,
            new FlightOrderOutcome(FlightOrderOutcomeKind.CONFIRMED, order, "El vuelo quedó confirmado."),
            now);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation(
            "Reconciliación: el vuelo {BookingId} existía en el proveedor y la reserva {ReservationId} quedó confirmada.",
            booking.Id, booking.ReservationId);

        return FlightReconciliationOutcome.CONFIRMED;
    }

    /// <summary>
    /// No existe ninguna orden y se agotaron los intentos: se libera el cupo del paquete y se cancela la
    /// reserva. Dejar el cupo tomado por una emisión que nunca ocurrió es el peor final posible, porque le
    /// saca el lugar a otra persona sin darle nada a nadie.
    /// </summary>
    private async Task<FlightReconciliationOutcome> CompensateAsync(
        FlightBooking booking, DateTimeOffset now, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        booking.Status = FlightBookingStatus.FAILED;
        booking.FailedAt = now;
        booking.FailureReason = "El proveedor no registró ninguna orden para esta compra.";
        booking.NextReconciliationAt = null;

        var won = await db.Reservations
            .Where(r => r.Id == booking.ReservationId && r.Status == ReservationStatus.PENDING_PAYMENT)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ReservationStatus.CANCELLED)
                .SetProperty(r => r.CancelledAt, now), ct);

        if (won == 1)
        {
            var items = await db.ReservationItems
                .AsNoTracking()
                .Where(i => i.ReservationId == booking.ReservationId && i.Status == ReservationItemStatus.PENDING_PAYMENT)
                .ToListAsync(ct);

            await bookingService.ReleaseHoldsAsync(items, ct);

            await db.ReservationItems
                .Where(i => i.ReservationId == booking.ReservationId && i.Status == ReservationItemStatus.PENDING_PAYMENT)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Status, ReservationItemStatus.CANCELLED)
                    .SetProperty(i => i.CancelledAt, now), ct);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // El pago de TurisClick es simulado: no hay dinero que devolver. La llamada existe igual para que
        // el día que haya una pasarela real, la compensación tenga un único lugar donde ocurrir.
        await paymentGateway.VoidAsync(
            new PaymentVoidRequest(booking.ReservationId, booking.TotalAmount, booking.Currency,
                "El proveedor aéreo no registró ninguna orden."), ct);

        logger.LogWarning(
            "Reconciliación: no existe orden para el vuelo {BookingId}; la reserva {ReservationId} se canceló y el cupo volvió al catálogo.",
            booking.Id, booking.ReservationId);

        return FlightReconciliationOutcome.FAILED;
    }

    /// <summary>
    /// Existe la orden pero la reserva ya no está vigente: se intenta cancelarla en el proveedor. Si no se
    /// puede, queda registrado con el localizador — una persona lo resuelve, pero nunca se pierde el rastro.
    /// </summary>
    private async Task<FlightReconciliationOutcome> CancelOrphanOrderAsync(
        FlightBooking booking, FlightOrderResult order, DateTimeOffset now, CancellationToken ct)
    {
        booking.ProviderOrderId = order.OrderId;
        booking.BookingReference = order.BookingReference;
        FlightBookingOrchestrator.ApplyItinerarySnapshot(booking, order);

        try
        {
            // Acá no hay nada que mostrarle a nadie: el presupuesto se pide sólo porque el proveedor exige
            // crear la cancelación antes de confirmarla.
            var quoted = await flightProvider.QuoteCancellationAsync(order.OrderId, ct);
            var cancellation = await flightProvider.ConfirmCancellationAsync(quoted.CancellationId, ct);

            booking.Status = FlightBookingStatus.CANCELLED;
            booking.FailureReason = "La reserva ya no estaba vigente: el pasaje se canceló en la aerolínea.";
            booking.NextReconciliationAt = null;

            logger.LogWarning(
                "Reconciliación: la orden {OrderId} quedó huérfana (reserva {ReservationId} no vigente) y se canceló. Reintegro informado: {Refund} {Currency}.",
                order.OrderId, booking.ReservationId, cancellation.RefundAmount, cancellation.RefundCurrency);

            await db.SaveChangesAsync(ct);
            return FlightReconciliationOutcome.ORDER_CANCELLED;
        }
        catch (FlightProviderException ex)
        {
            // No se puede cancelar y no se puede inventar que se canceló: queda pendiente y con rastro.
            logger.LogError(
                ex, "Reconciliación: la orden {OrderId} quedó huérfana y la aerolínea no aceptó cancelarla. Requiere revisión manual.",
                order.OrderId);

            booking.FailureReason =
                $"Pasaje emitido sin reserva vigente (localizador {order.BookingReference}); la cancelación con la aerolínea quedó pendiente.";

            return await ScheduleRetryAsync(booking, now, ct);
        }
    }
}
