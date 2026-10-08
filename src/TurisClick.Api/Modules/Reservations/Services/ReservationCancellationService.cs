using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Flights.Entities;
using TurisClick.Api.Modules.Flights.Services;
using TurisClick.Api.Modules.Reservations.Dtos;
using TurisClick.Api.Modules.Reservations.Entities;
using TurisClick.Api.Modules.Reservations.Payments;
using TurisClick.Api.Modules.Reservations.Policies;
using TurisClick.Api.Modules.Reservations.Repositories;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Reservations.Services;

public interface IReservationCancellationService
{
    /// <summary>Calcula qué pasaría si se cancelara, y lo guarda. No cancela nada.</summary>
    Task<CancellationQuoteResponse> QuoteAsync(Guid reservationId, CancellationToken ct);

    /// <summary>Ejecuta la cancelación que la persona aceptó. Irreversible.</summary>
    Task<CancellationResultResponse> ConfirmAsync(Guid reservationId, Guid quoteId, CancellationToken ct);

    /// <summary>Retoma las cancelaciones que quedaron a medias. Lo llama el proceso de fondo.</summary>
    Task<int> ResolvePendingAsync(int batchSize, CancellationToken ct);

    Task<ReservationCancellationStatus> ResolveAsync(Guid cancellationId, CancellationToken ct);
}

/// <summary>
/// Cancelar una reserva confirmada: presupuestar, aceptar, ejecutar.
///
/// Las dos reglas de producto que ordenan todo lo demás:
///
/// 1. **Una cancelación de TurisClick y una cancelación de aerolínea no son el mismo evento financiero.**
///    El paquete se devuelve según la política que el operador configuró y que la reserva congeló; el pasaje
///    según lo que informa la aerolínea. Aplicarle a uno la regla del otro sería inventar plata ajena, así
///    que el cálculo es **por componente** y los totales se agregan sólo entre monedas iguales.
/// 2. **Nunca se promete un reembolso que el proveedor no confirmó.** Si la aerolínea no informa cuánto
///    devuelve, eso se dice: "no informado" no es "cero".
///
/// Y una regla técnica: PostgreSQL, la pasarela y la aerolínea no comparten transacción. El orden está
/// elegido por lo que queda roto si algo falla en el medio —ver <see cref="ConfirmAsync"/>—.
/// </summary>
public class ReservationCancellationService(
    TurisClickDbContext db,
    IReservationRepository reservationRepository,
    IReservationBookingService bookingService,
    IFlightProvider flightProvider,
    IPaymentGateway paymentGateway,
    IPaymentLedger ledger,
    ICurrentUserContext currentUser,
    ILogger<ReservationCancellationService> logger) : IReservationCancellationService
{
    /// <summary>
    /// Ventana propia del presupuesto. Corta a propósito: el reembolso aéreo y los días que faltan para el
    /// viaje cambian, y un cálculo de ayer no describe la cancelación de hoy.
    /// </summary>
    private static readonly TimeSpan QuoteWindow = TimeSpan.FromMinutes(15);

    private const int MaxResolutionAttempts = 4;

    private static readonly TimeSpan[] ResolutionBackoff =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15)];

    // ================================================================ presupuesto

    public async Task<CancellationQuoteResponse> QuoteAsync(Guid reservationId, CancellationToken ct)
    {
        var reservation = await LoadOwnedAsync(reservationId, ct);

        if (reservation.Status == ReservationStatus.CANCELLING)
            throw new ConflictAppException(
                "Ya estamos procesando la cancelación de esta reserva.", ErrorCodes.CancellationInProgress);

        if (reservation.Status != ReservationStatus.CONFIRMED)
            throw new ConflictAppException(
                $"Una reserva en estado {reservation.Status} no se cancela con un presupuesto de reembolso.",
                ErrorCodes.ReservationNotCancellable);

        var flight = await db.FlightBookings
            .Include(b => b.FlightQuote)
            .FirstOrDefaultAsync(b => b.ReservationId == reservationId, ct);

        if (flight is not null && flight.Status is FlightBookingStatus.ORDERING or FlightBookingStatus.RECONCILIATION_REQUIRED)
            throw new ConflictAppException(
                "Estamos confirmando el vuelo de esta reserva. Vas a poder cancelarla en unos minutos.",
                ErrorCodes.FlightBookingInProgress);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var lines = new List<ReservationCancellationLine>();

        foreach (var item in reservation.Items.Where(i => i.Status == ReservationItemStatus.CONFIRMED))
            lines.Add(BuildProductLine(item, today));

        if (lines.Count == 0)
            throw new ConflictAppException(
                "Esta reserva no tiene servicios activos para cancelar.", ErrorCodes.ReservationNotCancellable);

        var expiresAt = DateTimeOffset.UtcNow.Add(QuoteWindow);
        string? providerCancellationId = null;

        if (flight is { Status: FlightBookingStatus.CONFIRMED, ProviderOrderId: { Length: > 0 } orderId })
        {
            var (flightLine, cancellationId, providerExpiry) = await QuoteFlightAsync(flight, orderId, ct);
            lines.Add(flightLine);
            providerCancellationId = cancellationId;

            // Manda el plazo más corto: el de la aerolínea, si es anterior al nuestro. Confirmar después de
            // su vencimiento simplemente no funciona.
            if (providerExpiry is { } providerLimit && providerLimit < expiresAt) expiresAt = providerLimit;
        }

        // Un presupuesto nuevo invalida al anterior, y no es una decisión nuestra: la aerolínea sólo permite
        // confirmar la última cancelación creada para una orden, así que dejar dos "vigentes" sería ofrecer
        // uno que ya no se puede ejecutar.
        await db.ReservationCancellations
            .Where(c => c.ReservationId == reservationId && c.Status == ReservationCancellationStatus.QUOTED)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, ReservationCancellationStatus.EXPIRED), ct);

        var cancellation = new ReservationCancellation
        {
            Id = Guid.NewGuid(),
            ReservationId = reservationId,
            Status = ReservationCancellationStatus.QUOTED,
            ExpiresAt = expiresAt,
            ProviderCancellationId = providerCancellationId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        foreach (var line in lines)
        {
            line.Id = Guid.NewGuid();
            line.CancellationId = cancellation.Id;
            cancellation.Lines.Add(line);
        }

        db.ReservationCancellations.Add(cancellation);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Reserva {ReservationId}: presupuesto de cancelación {QuoteId} con {Lines} línea(s), vence {ExpiresAt}.",
            reservationId, cancellation.Id, cancellation.Lines.Count, expiresAt);

        return ToQuoteResponse(cancellation);
    }

    /// <summary>
    /// La línea de un producto. El reembolso sale de la política **congelada en la reserva**, no de la que
    /// el operador tenga hoy: lo que se aceptó al comprar es lo que vale.
    /// </summary>
    private static ReservationCancellationLine BuildProductLine(ReservationItem item, DateOnly today)
    {
        var component = item.ProductType == ProductType.PACKAGE ? PaymentComponent.PACKAGE : PaymentComponent.EXPERIENCE;
        var label = item.Package?.Title ?? item.Experience?.Title ?? "Servicio";

        if (!CancellationPolicy.TryParse(item.CancellationPolicy, out var policy))
            throw new ConflictAppException(
                $"\"{label}\" no tiene una política de cancelación definida por el operador, así que no se puede " +
                "cancelar desde la app. Escribile al operador para resolverlo.",
                ErrorCodes.CancellationPolicyMissing);

        var startDate = item.PackageAvailability?.DepartureDate ?? item.ExperienceAvailability?.Date;
        var daysBefore = startDate is { } start ? start.DayNumber - today.DayNumber : 0;

        var percentage = policy!.ResolvePercentage(Math.Max(daysBefore, 0));
        var refund = Math.Round(item.Subtotal * percentage / 100m, 2, MidpointRounding.ToZero);

        return new ReservationCancellationLine
        {
            Component = component,
            ReservationItemId = item.Id,
            Label = label,
            PaidAmount = item.Subtotal,
            RefundAmount = refund,
            FeeAmount = item.Subtotal - refund,
            Currency = item.Currency,
            PolicyApplied = item.CancellationPolicy,
            RefundPercentage = percentage,
            RefundKnown = true,
            Explanation = percentage switch
            {
                100 => $"Cancelás {Anticipacion(daysBefore)}: se devuelve todo.",
                0 => $"Cancelás {Anticipacion(daysBefore)}: la política del operador no devuelve nada.",
                _ => $"Cancelás {Anticipacion(daysBefore)}: el operador devuelve el {percentage}%.",
            },
        };
    }

    /// <summary>
    /// La línea del vuelo. El importe lo dice la aerolínea: acá no se calcula ningún porcentaje ni se aplica
    /// la política del operador terrestre.
    /// </summary>
    private async Task<(ReservationCancellationLine Line, string CancellationId, DateTimeOffset? ExpiresAt)> QuoteFlightAsync(
        FlightBooking flight, string orderId, CancellationToken ct)
    {
        FlightCancellationResult quote;
        try
        {
            quote = await flightProvider.QuoteCancellationAsync(orderId, ct);
        }
        catch (FlightProviderException ex)
        {
            // Sin saber qué hace la aerolínea no se puede presupuestar nada: cancelar el paquete y dejar el
            // pasaje vivo sería lo peor de los dos mundos.
            logger.LogWarning(ex, "No se pudo presupuestar la cancelación del vuelo de la reserva {ReservationId}.", flight.ReservationId);

            throw new ConflictAppException(
                "No pudimos consultar a la aerolínea cuánto devuelve por este pasaje. Probá de nuevo en unos minutos.",
                ErrorCodes.FlightProviderUnreachable);
        }

        var label = $"Vuelo {flight.OriginIata} → {flight.DestinationIata}";

        // Moneda distinta a la cobrada: no se convierte ni se compara. Se trata como importe no informado y
        // se dice, que es lo único honesto que se puede hacer sin un tipo de cambio.
        var sameCurrency = quote.RefundCurrency is null
            || string.Equals(quote.RefundCurrency, flight.Currency, StringComparison.OrdinalIgnoreCase);

        var known = quote.RefundAmount is not null && sameCurrency;
        var refund = known ? Math.Min(quote.RefundAmount!.Value, flight.TotalAmount) : 0m;

        return (new ReservationCancellationLine
        {
            Component = PaymentComponent.FLIGHT,
            Label = label,
            PaidAmount = flight.TotalAmount,
            RefundAmount = refund,
            FeeAmount = known ? flight.TotalAmount - refund : 0m,
            Currency = flight.Currency,
            RefundKnown = known,
            Explanation = !known
                ? "La aerolínea no informó cuánto devuelve por este pasaje. Lo vas a ver cuando lo confirme."
                : refund == 0m
                    ? "La aerolínea no devuelve nada por este pasaje."
                    : refund == flight.TotalAmount
                        ? "La aerolínea devuelve el total del pasaje."
                        : $"La aerolínea devuelve {refund:0.00} {flight.Currency} y retiene el resto como cargo de cancelación.",
        }, quote.CancellationId, quote.ExpiresAt);
    }

    // ================================================================ ejecución

    /// <summary>
    /// Ejecuta la cancelación aceptada.
    ///
    /// El orden importa y está elegido por lo que queda roto si algo falla:
    ///
    /// <list type="number">
    /// <item><b>Se marca CANCELLING y la cancelación como aceptada, y se commitea.</b> Si el proceso muere
    /// después, queda rastro de que había una cancelación en curso en vez de una reserva que parece vigente
    /// con un pasaje cancelado.</item>
    /// <item><b>Se cancela el pasaje en la aerolínea, primero.</b> Si se liberara el cupo del paquete antes y
    /// la aerolínea rechazara, le habríamos regalado el lugar a otra persona mientras esta sigue con un
    /// pasaje: eso no se puede deshacer. Al revés sí: un pasaje cancelado sobre una reserva todavía viva es
    /// visible y resoluble.</item>
    /// <item><b>Una transacción local</b> cancela la reserva, libera el cupo exactamente una vez y guarda lo
    /// que la aerolínea devolvió de verdad.</item>
    /// <item><b>Los reembolsos</b> van al final, con clave idempotente. Si fallan, lo cancelado sigue
    /// cancelado y la cancelación queda en REFUND_PENDING: no se finge que la plata volvió.</item>
    /// </list>
    /// </summary>
    public async Task<CancellationResultResponse> ConfirmAsync(Guid reservationId, Guid quoteId, CancellationToken ct)
    {
        var reservation = await LoadOwnedAsync(reservationId, ct);

        var cancellation = await db.ReservationCancellations
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.Id == quoteId, ct)
            ?? throw new NotFoundAppException("Presupuesto de cancelación no encontrado.", ErrorCodes.CancellationQuoteInvalid);

        if (cancellation.ReservationId != reservationId)
            throw new NotFoundAppException("Presupuesto de cancelación no encontrado.", ErrorCodes.CancellationQuoteInvalid);

        if (cancellation.Status != ReservationCancellationStatus.QUOTED)
            throw new ConflictAppException(
                "Ese presupuesto ya no está vigente. Pedí uno nuevo para ver el reembolso actual.",
                ErrorCodes.CancellationQuoteInvalid);

        if (cancellation.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            cancellation.Status = ReservationCancellationStatus.EXPIRED;
            await db.SaveChangesAsync(ct);

            throw new ConflictAppException(
                "El presupuesto de cancelación venció. Pedí uno nuevo: el reembolso puede haber cambiado.",
                ErrorCodes.CancellationQuoteExpired);
        }

        // ---- 1. Aceptación: se escribe ANTES de tocar la aerolínea ----
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var won = await db.Reservations
                .Where(r => r.Id == reservationId && r.Status == ReservationStatus.CONFIRMED)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReservationStatus.CANCELLING), ct);

            if (won != 1)
            {
                await tx.RollbackAsync(ct);

                // El doble toque termina acá: la segunda ejecución no gana la transición y no cancela nada.
                throw new ConflictAppException(
                    "La reserva cambió de estado mientras se cancelaba; volvé a consultarla.",
                    ErrorCodes.CancellationInProgress);
            }

            cancellation.Status = ReservationCancellationStatus.ACCEPTED;
            cancellation.AcceptedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // ---- 2. La aerolínea, primero ----
        var flightLine = cancellation.Lines.FirstOrDefault(l => l.Component == PaymentComponent.FLIGHT);

        if (flightLine is not null)
        {
            var outcome = await CancelFlightAsync(cancellation, flightLine, ct);
            if (outcome is not null) return outcome;
        }

        // ---- 3 y 4 ----
        return await FinishCancellationAsync(cancellation, ct);
    }

    /// <summary>
    /// Confirma la cancelación en la aerolínea. Devuelve null si salió bien (y el flujo sigue), o la
    /// respuesta final cuando el desenlace impide continuar.
    /// </summary>
    private async Task<CancellationResultResponse?> CancelFlightAsync(
        ReservationCancellation cancellation, ReservationCancellationLine flightLine, CancellationToken ct)
    {
        if (cancellation.ProviderCancellationId is not { Length: > 0 } providerCancellationId)
        {
            // No debería pasar: una línea de vuelo nace junto a su cancelación presupuestada.
            logger.LogError(
                "Cancelación {CancellationId} tiene línea de vuelo sin identificador del proveedor.", cancellation.Id);

            return await MarkRequiresReviewAsync(
                cancellation, "Faltaba el identificador de la cancelación aérea.", ct);
        }

        try
        {
            var confirmed = await flightProvider.ConfirmCancellationAsync(providerCancellationId, ct);

            cancellation.FlightCancelledAt = confirmed.ConfirmedAt ?? DateTimeOffset.UtcNow;

            // Lo que la aerolínea devolvió **de verdad** manda sobre lo presupuestado. Normalmente coincide;
            // cuando no, se guarda el real y la respuesta lo muestra.
            if (confirmed.RefundAmount is { } actual
                && (confirmed.RefundCurrency is null
                    || string.Equals(confirmed.RefundCurrency, flightLine.Currency, StringComparison.OrdinalIgnoreCase)))
            {
                var bounded = Math.Min(actual, flightLine.PaidAmount);

                if (bounded != flightLine.RefundAmount || !flightLine.RefundKnown)
                    logger.LogInformation(
                        "Cancelación {CancellationId}: la aerolínea confirmó {Actual} y se había presupuestado {Quoted}.",
                        cancellation.Id, bounded, flightLine.RefundAmount);

                flightLine.RefundAmount = bounded;
                flightLine.FeeAmount = flightLine.PaidAmount - bounded;
                flightLine.RefundKnown = true;
            }

            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (FlightProviderRequestException ex)
        {
            // La aerolínea contestó que no. No se canceló nada, así que la reserva vuelve a estar vigente:
            // es la única lectura honesta del estado.
            logger.LogWarning(
                ex, "La aerolínea rechazó la cancelación {CancellationId} (código {ProviderCode}).",
                cancellation.Id, ex.ProviderCode);

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            await db.Reservations
                .Where(r => r.Id == cancellation.ReservationId && r.Status == ReservationStatus.CANCELLING)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReservationStatus.CONFIRMED), ct);

            cancellation.Status = ReservationCancellationStatus.FAILED;
            cancellation.FailureReason = "La aerolínea no aceptó cancelar el pasaje.";
            cancellation.CompletedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new CancellationResultResponse
            {
                ReservationId = cancellation.ReservationId,
                Status = cancellation.Status.ToString(),
                Message =
                    "La aerolínea no aceptó cancelar el pasaje, así que no cancelamos nada: tu reserva sigue vigente " +
                    "y no se te devolvió ni se te cobró nada. Escribinos para resolverlo.",
                Lines = [.. cancellation.Lines.Select(ToLineResponse)],
                FlightCancelled = false,
            };
        }
        catch (FlightProviderException ex)
        {
            // Desenlace desconocido: la solicitud pudo haber llegado. Ni se libera cupo ni se reintenta a
            // ciegas; queda marcado y lo resuelve el proceso de resolución.
            logger.LogError(ex, "Desenlace desconocido al cancelar el vuelo de la cancelación {CancellationId}.", cancellation.Id);

            return await MarkRequiresReviewAsync(
                cancellation, "No pudimos confirmar con la aerolínea si el pasaje quedó cancelado.", ct);
        }
    }

    /// <summary>Cancela localmente, libera el cupo exactamente una vez y después reembolsa.</summary>
    private async Task<CancellationResultResponse> FinishCancellationAsync(
        ReservationCancellation cancellation, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var reservationId = cancellation.ReservationId;

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            // Las líneas se leen DENTRO de la transacción y sólo las que siguen confirmadas: es lo que hace
            // que el cupo se devuelva una vez y no una por intento.
            var activeItems = await db.ReservationItems
                .AsNoTracking()
                .Where(i => i.ReservationId == reservationId && i.Status == ReservationItemStatus.CONFIRMED)
                .ToListAsync(ct);

            await bookingService.ReleaseHoldsAsync(activeItems, ct);

            await db.ReservationItems
                .Where(i => i.ReservationId == reservationId && i.Status == ReservationItemStatus.CONFIRMED)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Status, ReservationItemStatus.CANCELLED)
                    .SetProperty(i => i.CancelledAt, now), ct);

            await db.Reservations
                .Where(r => r.Id == reservationId && r.Status == ReservationStatus.CANCELLING)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, ReservationStatus.CANCELLED)
                    .SetProperty(r => r.CancelledAt, now), ct);

            if (cancellation.FlightCancelledAt is not null)
                await db.FlightBookings
                    .Where(b => b.ReservationId == reservationId && b.Status == FlightBookingStatus.CONFIRMED)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(b => b.Status, FlightBookingStatus.CANCELLED)
                        .SetProperty(b => b.FailureReason, "El pasaje se canceló a pedido del viajero."), ct);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            logger.LogInformation(
                "Reserva {ReservationId} cancelada: {Items} línea(s) liberada(s).", reservationId, activeItems.Count);
        }

        return await RefundAsync(cancellation, ct);
    }

    /// <summary>
    /// Devuelve la plata de cada línea con reembolso. Cada reembolso lleva una clave derivada de la
    /// cancelación y del componente: reintentarlo no puede devolver dos veces lo mismo.
    /// </summary>
    private async Task<CancellationResultResponse> RefundAsync(ReservationCancellation cancellation, CancellationToken ct)
    {
        var failures = new List<string>();

        foreach (var line in cancellation.Lines.Where(l => l.RefundKnown && l.RefundAmount > 0m))
        {
            var key = RefundKey(cancellation.Id, line);

            if (await ledger.RefundAlreadyRecordedAsync(key, ct)) continue;

            PaymentRefundResult result;
            try
            {
                result = await paymentGateway.RefundAsync(
                    new PaymentRefundRequest(
                        cancellation.ReservationId,
                        line.RefundAmount,
                        line.Currency,
                        $"Cancelación de {line.Label}",
                        key),
                    ct);
            }
            catch (Exception ex)
            {
                // Una excepción de la pasarela no puede tumbar la cancelación: lo cancelado ya está
                // cancelado. Se registra el intento fallido y se reintenta después.
                logger.LogError(ex, "Falló el reembolso de la cancelación {CancellationId}.", cancellation.Id);
                result = new PaymentRefundResult(false, "La pasarela no respondió.", null);
            }

            ledger.RecordRefund(
                cancellation.ReservationId,
                line.RefundAmount,
                line.Currency,
                line.Component,
                line.ReservationItemId,
                "Simulated",
                key,
                result.Succeeded,
                result.FailureReason,
                result.Reference);

            if (!result.Succeeded) failures.Add(line.Label);
        }

        var now = DateTimeOffset.UtcNow;

        if (failures.Count > 0)
        {
            cancellation.Status = ReservationCancellationStatus.REFUND_PENDING;
            cancellation.FailureReason = $"Reembolso pendiente de: {string.Join(", ", failures)}.";
            cancellation.NextResolutionAt = now.Add(ResolutionBackoff[0]);
        }
        else
        {
            cancellation.Status = ReservationCancellationStatus.COMPLETED;
            cancellation.CompletedAt = now;
            cancellation.NextResolutionAt = null;
        }

        await db.SaveChangesAsync(ct);

        return ToResultResponse(cancellation);
    }

    private async Task<CancellationResultResponse> MarkRequiresReviewAsync(
        ReservationCancellation cancellation, string reason, CancellationToken ct)
    {
        cancellation.Status = ReservationCancellationStatus.REQUIRES_REVIEW;
        cancellation.FailureReason = reason;
        cancellation.NextResolutionAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        return new CancellationResultResponse
        {
            ReservationId = cancellation.ReservationId,
            Status = cancellation.Status.ToString(),
            Message =
                "Estamos terminando de confirmar la cancelación con la aerolínea. Te avisamos en unos minutos; " +
                "no hace falta volver a intentar.",
            Lines = [.. cancellation.Lines.Select(ToLineResponse)],
            FlightCancelled = cancellation.FlightCancelledAt is not null,
        };
    }

    // ================================================================ resolución de lo que quedó a medias

    public async Task<int> ResolvePendingAsync(int batchSize, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var candidates = await db.ReservationCancellations
            .AsNoTracking()
            .Where(c => (c.Status == ReservationCancellationStatus.REQUIRES_REVIEW
                    || c.Status == ReservationCancellationStatus.REFUND_PENDING
                    || c.Status == ReservationCancellationStatus.ACCEPTED)
                && (c.NextResolutionAt == null || c.NextResolutionAt <= now))
            .OrderBy(c => c.CreatedAt)
            .Take(Math.Clamp(batchSize, 1, 200))
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (candidates.Count == 0) return 0;

        var resolved = 0;
        foreach (var id in candidates)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (await ResolveAsync(id, ct) == ReservationCancellationStatus.COMPLETED) resolved++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "No se pudo resolver la cancelación {CancellationId}; sigue el resto del lote.", id);
            }
        }

        return resolved;
    }

    /// <summary>
    /// Retoma una cancelación a medias. Igual que con la emisión: **nunca se repite la operación externa
    /// para averiguar qué pasó**; primero se le pregunta al proveedor si el pasaje ya está cancelado.
    /// </summary>
    public async Task<ReservationCancellationStatus> ResolveAsync(Guid cancellationId, CancellationToken ct)
    {
        var cancellation = await db.ReservationCancellations
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.Id == cancellationId, ct);

        if (cancellation is null) return ReservationCancellationStatus.EXPIRED;

        if (cancellation.Status is not (ReservationCancellationStatus.REFUND_PENDING
            or ReservationCancellationStatus.REQUIRES_REVIEW
            or ReservationCancellationStatus.ACCEPTED))
            return cancellation.Status;

        cancellation.ResolutionAttempts++;

        // Sólo falta la plata: se reintenta el reembolso, que es idempotente por clave.
        if (cancellation.Status == ReservationCancellationStatus.REFUND_PENDING)
        {
            var result = await RefundAsync(cancellation, ct);
            return Enum.Parse<ReservationCancellationStatus>(result.Status);
        }

        // Falta saber si el pasaje quedó cancelado del otro lado.
        var flight = await db.FlightBookings.FirstOrDefaultAsync(b => b.ReservationId == cancellation.ReservationId, ct);

        if (flight?.ProviderOrderId is not { Length: > 0 } orderId)
        {
            // Sin vuelo no hay nada que preguntar: se termina la parte local.
            await FinishCancellationAsync(cancellation, ct);
            return cancellation.Status;
        }

        FlightOrderResult? order;
        try
        {
            order = await flightProvider.GetOrderAsync(orderId, ct);
        }
        catch (FlightProviderException ex)
        {
            logger.LogWarning(ex, "El proveedor no respondió por la cancelación {CancellationId}; se reagenda.", cancellationId);
            return await ScheduleRetryAsync(cancellation, ct);
        }

        if (order?.CancelledAt is not null)
        {
            // Sí quedó cancelado: se completa la parte local que había quedado pendiente.
            cancellation.FlightCancelledAt ??= order.CancelledAt;
            cancellation.Status = ReservationCancellationStatus.ACCEPTED;
            await db.SaveChangesAsync(ct);

            await FinishCancellationAsync(cancellation, ct);
            return cancellation.Status;
        }

        // No está cancelado. Reintentar la confirmación es seguro justamente porque lo verificamos.
        if (cancellation.ResolutionAttempts < MaxResolutionAttempts
            && cancellation.ProviderCancellationId is { Length: > 0 })
        {
            try
            {
                var confirmed = await flightProvider.ConfirmCancellationAsync(cancellation.ProviderCancellationId, ct);
                cancellation.FlightCancelledAt = confirmed.ConfirmedAt ?? DateTimeOffset.UtcNow;
                cancellation.Status = ReservationCancellationStatus.ACCEPTED;
                await db.SaveChangesAsync(ct);

                await FinishCancellationAsync(cancellation, ct);
                return cancellation.Status;
            }
            catch (FlightProviderException ex)
            {
                logger.LogWarning(ex, "Reintento de cancelación {CancellationId} sin éxito.", cancellationId);
                return await ScheduleRetryAsync(cancellation, ct);
            }
        }

        // Se agotaron los intentos. La reserva vuelve a estar vigente —no se canceló nada— y queda marcada
        // para que una persona lo resuelva. Esto NO se disimula como éxito.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.Reservations
            .Where(r => r.Id == cancellation.ReservationId && r.Status == ReservationStatus.CANCELLING)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReservationStatus.CONFIRMED), ct);

        cancellation.Status = ReservationCancellationStatus.REQUIRES_REVIEW;
        cancellation.FailureReason = "La aerolínea no confirmó la cancelación después de varios intentos.";
        cancellation.NextResolutionAt = null;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return cancellation.Status;
    }

    private async Task<ReservationCancellationStatus> ScheduleRetryAsync(
        ReservationCancellation cancellation, CancellationToken ct)
    {
        var index = Math.Clamp(cancellation.ResolutionAttempts - 1, 0, ResolutionBackoff.Length - 1);
        cancellation.NextResolutionAt = DateTimeOffset.UtcNow.Add(ResolutionBackoff[index]);

        await db.SaveChangesAsync(ct);
        return cancellation.Status;
    }

    // ================================================================ lectura y mapeo

    /// <summary>
    /// La reserva se lee **sin tracking**, y no es un detalle de rendimiento: este servicio sólo la cambia con
    /// UPDATE condicionales, así que una entidad trackeada sería una trampa — cualquier SaveChanges posterior
    /// reescribiría el estado que esos UPDATE acabaron de ganar.
    /// </summary>
    private async Task<Reservation> LoadOwnedAsync(Guid reservationId, CancellationToken ct)
    {
        var reservation = await reservationRepository.GetByIdForReadAsync(reservationId, ct)
            ?? throw new NotFoundAppException("Reserva no encontrada.");

        if (reservation.TouristId != currentUser.UserId)
            throw new ForbiddenAppException("Esta reserva no te pertenece.");

        return reservation;
    }

    private static string RefundKey(Guid cancellationId, ReservationCancellationLine line) =>
        $"ref_{cancellationId:N}_{line.Component}_{line.ReservationItemId?.ToString("N") ?? "flight"}";

    private static CancellationLineResponse ToLineResponse(ReservationCancellationLine line) => new()
    {
        Component = line.Component.ToString(),
        Label = line.Label,
        PaidAmount = line.PaidAmount,
        RefundAmount = line.RefundAmount,
        FeeAmount = line.FeeAmount,
        Currency = line.Currency,
        RefundPercentage = line.RefundPercentage,
        RefundKnown = line.RefundKnown,
        Explanation = line.Explanation,
    };

    internal static List<MoneyLineResponse> GroupByCurrency(
        IEnumerable<ReservationCancellationLine> lines, Func<ReservationCancellationLine, decimal> amount) =>
        [.. lines
            .GroupBy(l => l.Currency)
            .Select(g => new MoneyLineResponse { Currency = g.Key, Amount = g.Sum(amount) })
            .Where(m => m.Amount > 0m)
            .OrderBy(m => m.Currency)];

    private static CancellationQuoteResponse ToQuoteResponse(ReservationCancellation cancellation) => new()
    {
        QuoteId = cancellation.Id,
        ReservationId = cancellation.ReservationId,
        ExpiresAt = cancellation.ExpiresAt,
        Lines = [.. cancellation.Lines.Select(ToLineResponse)],
        Refunds = GroupByCurrency(cancellation.Lines, l => l.RefundAmount),
        Fees = GroupByCurrency(cancellation.Lines, l => l.FeeAmount),
        HasUnknownRefund = cancellation.Lines.Any(l => !l.RefundKnown),
        Summary = BuildSummary(cancellation),
    };

    /// <summary>
    /// El resumen lo decide el dominio: dos apps distintas no pueden contar historias distintas sobre el
    /// mismo reembolso. Y si falta un importe, se dice que falta en lugar de mostrar un total incompleto.
    /// </summary>
    /// <summary>
    /// "con 35 días de anticipación" / "el mismo día". Antes decía "35 día(s)", que es una plantilla sin
    /// resolver: el viajero lee esta frase tal cual en la app.
    /// </summary>
    private static string Anticipacion(int daysBefore) => daysBefore switch
    {
        <= 0 => "el mismo día de la salida",
        1 => "con 1 día de anticipación",
        _ => $"con {daysBefore} días de anticipación",
    };

    /// <summary>
    /// La plata se escribe como se escribe en Bolivia. El resumen viajaba como "3000.00 BOB" y en la app
    /// quedaba al lado de "Bs 3.000,00", como si fueran dos importes distintos.
    /// </summary>
    private static string FormatMoney(decimal amount, string currency)
    {
        var cultura = CultureInfo.GetCultureInfo("es-BO");
        var simbolo = currency switch { "BOB" => "Bs", "USD" => "USD", "EUR" => "EUR", _ => currency };
        return $"{simbolo} {amount.ToString("N2", cultura)}";
    }

    private static string BuildSummary(ReservationCancellation cancellation)
    {
        var refunds = GroupByCurrency(cancellation.Lines, l => l.RefundAmount);
        var unknown = cancellation.Lines.Any(l => !l.RefundKnown);

        if (refunds.Count == 0)
            return unknown
                ? "La aerolínea todavía no informó cuánto devuelve, y el resto de la reserva no es reembolsable."
                : "Esta cancelación no tiene reembolso: ningún componente es reembolsable en esta fecha.";

        var amounts = string.Join(" + ", refunds.Select(r => FormatMoney(r.Amount, r.Currency)));

        return unknown
            ? $"Reembolso confirmado: {amounts}. Falta lo que informe la aerolínea."
            : $"Reembolso total: {amounts}.";
    }

    private static CancellationResultResponse ToResultResponse(ReservationCancellation cancellation) => new()
    {
        ReservationId = cancellation.ReservationId,
        Status = cancellation.Status.ToString(),
        Message = cancellation.Status switch
        {
            ReservationCancellationStatus.COMPLETED =>
                $"Cancelación completada. {BuildSummary(cancellation)}",
            ReservationCancellationStatus.REFUND_PENDING =>
                "La reserva quedó cancelada. El reembolso está en proceso: te avisamos cuando se acredite.",
            _ => "La cancelación quedó en revisión. Te avisamos en cuanto se resuelva.",
        },
        Lines = [.. cancellation.Lines.Select(ToLineResponse)],
        Refunds = GroupByCurrency(cancellation.Lines.Where(l => l.RefundKnown), l => l.RefundAmount),
        FlightCancelled = cancellation.FlightCancelledAt is not null,
        CompletedAt = cancellation.CompletedAt,
    };
}
