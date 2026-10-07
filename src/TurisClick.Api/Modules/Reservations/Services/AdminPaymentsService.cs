using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Reservations.Dtos;
using TurisClick.Api.Modules.Reservations.Entities;
using TurisClick.Api.Modules.Reservations.Payments;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Reservations.Services;

public interface IAdminPaymentsService
{
    Task<ReservationPaymentsResponse> GetReservationPaymentsAsync(Guid reservationId, CancellationToken ct);
    Task<List<CancellationSummaryResponse>> ListUnresolvedCancellationsAsync(int limit, CancellationToken ct);
}

/// <summary>
/// Lectura del libro de pagos y de las cancelaciones para el ADMIN. Sólo lee: nada acá cambia un estado, y
/// eso es deliberado —resolver una cancelación a medias es una operación de dominio, no una edición manual
/// de la plata—.
/// </summary>
public class AdminPaymentsService(TurisClickDbContext db, IPaymentLedger ledger) : IAdminPaymentsService
{
    public async Task<ReservationPaymentsResponse> GetReservationPaymentsAsync(Guid reservationId, CancellationToken ct)
    {
        var reservation = await db.Reservations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == reservationId, ct)
            ?? throw new NotFoundAppException("Reserva no encontrada.");

        var transactions = await db.PaymentTransactions
            .AsNoTracking()
            .Where(p => p.ReservationId == reservationId)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(ct);

        var cancellations = await db.ReservationCancellations
            .AsNoTracking()
            .Include(c => c.Lines)
            .Where(c => c.ReservationId == reservationId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        var balances = await ledger.GetBalanceAsync(reservationId, ct);

        return new ReservationPaymentsResponse
        {
            ReservationId = reservationId,
            ReservationStatus = reservation.Status.ToString(),
            Balances = [.. balances.Select(b => new PaymentBalanceResponse
            {
                Currency = b.Currency,
                Charged = b.Charged,
                Refunded = b.Refunded,
                Net = b.Net,
            })],
            // El libro completo, intentos fallidos incluidos: sin ellos no se puede reconstruir qué pasó.
            Transactions = [.. transactions.Select(ToResponse)],
            Cancellations = [.. cancellations.Select(ToSummary)],
        };
    }

    public async Task<List<CancellationSummaryResponse>> ListUnresolvedCancellationsAsync(int limit, CancellationToken ct)
    {
        var cancellations = await db.ReservationCancellations
            .AsNoTracking()
            .Include(c => c.Lines)
            .Where(c => c.Status == ReservationCancellationStatus.REFUND_PENDING
                || c.Status == ReservationCancellationStatus.REQUIRES_REVIEW
                || c.Status == ReservationCancellationStatus.ACCEPTED)
            .OrderBy(c => c.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);

        return [.. cancellations.Select(ToSummary)];
    }

    private static PaymentTransactionResponse ToResponse(PaymentTransaction transaction) => new()
    {
        Id = transaction.Id,
        Type = transaction.Type.ToString(),
        Status = transaction.Status.ToString(),
        Amount = transaction.Amount,
        Currency = transaction.Currency,
        Component = transaction.Component?.ToString(),
        Provider = transaction.Provider,
        FailureReason = transaction.FailureReason,
        CreatedAt = transaction.CreatedAt,
    };

    internal static CancellationSummaryResponse ToSummary(ReservationCancellation cancellation) => new()
    {
        Id = cancellation.Id,
        ReservationId = cancellation.ReservationId,
        Status = cancellation.Status.ToString(),
        CreatedAt = cancellation.CreatedAt,
        AcceptedAt = cancellation.AcceptedAt,
        CompletedAt = cancellation.CompletedAt,
        FlightCancelled = cancellation.FlightCancelledAt is not null,
        FailureReason = cancellation.FailureReason,
        ResolutionAttempts = cancellation.ResolutionAttempts,
        Lines = [.. cancellation.Lines.Select(line => new CancellationLineResponse
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
        })],
    };
}
