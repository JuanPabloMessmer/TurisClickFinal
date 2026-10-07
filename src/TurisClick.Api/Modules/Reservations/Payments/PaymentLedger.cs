using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Reservations.Entities;

namespace TurisClick.Api.Modules.Reservations.Payments;

/// <summary>
/// Escribe el libro de movimientos de dinero de una reserva y lo lee resumido.
///
/// **No es un decorador del gateway, y es deliberado.** Un decorador tendría que guardar la fila en el
/// momento de la llamada, fuera de la transacción de quien lo invoca; acá hace falta lo contrario: que el
/// movimiento y el cambio de estado que lo justifica se commiteen **juntos**. Por eso estos métodos agregan
/// la entidad y **no** llaman a SaveChanges: la frontera transaccional es del caller.
/// </summary>
public interface IPaymentLedger
{
    /// <summary>Registra un intento de cobro —aprobado o rechazado—. Un rechazo también es historia.</summary>
    PaymentTransaction RecordCharge(
        Guid reservationId, decimal amount, string currency, string provider, bool approved, string? failureReason);

    /// <summary>Registra el reverso de un cobro que no se llegó a capturar.</summary>
    PaymentTransaction RecordVoid(Guid reservationId, decimal amount, string currency, string provider, string reason);

    /// <summary>
    /// Registra un reembolso con su componente de origen. La clave es única entre reembolsos: un reintento
    /// choca con el índice en vez de devolver la plata dos veces.
    /// </summary>
    PaymentTransaction RecordRefund(
        Guid reservationId,
        decimal amount,
        string currency,
        PaymentComponent component,
        Guid? reservationItemId,
        string provider,
        string idempotencyKey,
        bool succeeded,
        string? failureReason,
        string? providerReference);

    /// <summary>
    /// Cuánto se cobró, cuánto se devolvió y qué queda, **por moneda**. Nunca un único total: sumar monedas
    /// distintas exigiría un tipo de cambio que TurisClick no tiene.
    /// </summary>
    Task<List<PaymentBalance>> GetBalanceAsync(Guid reservationId, CancellationToken ct);

    Task<bool> RefundAlreadyRecordedAsync(string idempotencyKey, CancellationToken ct);
}

/// <summary>Saldo de una reserva en una moneda. `Net` es lo que quedó efectivamente pagado.</summary>
public record PaymentBalance(string Currency, decimal Charged, decimal Refunded)
{
    public decimal Net => Charged - Refunded;
}

public class PaymentLedger(TurisClickDbContext db) : IPaymentLedger
{
    public PaymentTransaction RecordCharge(
        Guid reservationId, decimal amount, string currency, string provider, bool approved, string? failureReason)
        => Add(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            ReservationId = reservationId,
            Type = PaymentTransactionType.CHARGE,
            Status = approved ? PaymentTransactionStatus.SUCCEEDED : PaymentTransactionStatus.FAILED,
            Amount = amount,
            Currency = currency,
            Provider = provider,
            // Los cobros no compiten por unicidad: cada intento es un evento distinto y todos se guardan.
            IdempotencyKey = $"chg_{reservationId:N}_{currency}_{Guid.NewGuid():N}",
            FailureReason = Truncate(failureReason),
            CreatedAt = DateTimeOffset.UtcNow,
        });

    public PaymentTransaction RecordVoid(
        Guid reservationId, decimal amount, string currency, string provider, string reason)
        => Add(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            ReservationId = reservationId,
            Type = PaymentTransactionType.VOID,
            Status = PaymentTransactionStatus.SUCCEEDED,
            Amount = amount,
            Currency = currency,
            Provider = provider,
            IdempotencyKey = $"void_{reservationId:N}_{Guid.NewGuid():N}",
            FailureReason = Truncate(reason),
            CreatedAt = DateTimeOffset.UtcNow,
        });

    public PaymentTransaction RecordRefund(
        Guid reservationId,
        decimal amount,
        string currency,
        PaymentComponent component,
        Guid? reservationItemId,
        string provider,
        string idempotencyKey,
        bool succeeded,
        string? failureReason,
        string? providerReference)
        => Add(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            ReservationId = reservationId,
            Type = PaymentTransactionType.REFUND,
            Status = succeeded ? PaymentTransactionStatus.SUCCEEDED : PaymentTransactionStatus.FAILED,
            Amount = amount,
            Currency = currency,
            Component = component,
            ReservationItemId = reservationItemId,
            Provider = provider,
            ProviderReference = providerReference,
            IdempotencyKey = idempotencyKey,
            FailureReason = Truncate(failureReason),
            CreatedAt = DateTimeOffset.UtcNow,
        });

    public async Task<List<PaymentBalance>> GetBalanceAsync(Guid reservationId, CancellationToken ct)
    {
        // Sólo los movimientos exitosos cuentan para el saldo; los fallidos quedan en el libro como historia
        // pero no movieron plata.
        var rows = await db.PaymentTransactions
            .AsNoTracking()
            .Where(p => p.ReservationId == reservationId && p.Status == PaymentTransactionStatus.SUCCEEDED)
            .Select(p => new { p.Type, p.Amount, p.Currency })
            .ToListAsync(ct);

        return [.. rows
            .GroupBy(r => r.Currency)
            .Select(g => new PaymentBalance(
                g.Key,
                g.Where(r => r.Type == PaymentTransactionType.CHARGE).Sum(r => r.Amount),
                g.Where(r => r.Type is PaymentTransactionType.REFUND or PaymentTransactionType.VOID).Sum(r => r.Amount)))
            .OrderBy(b => b.Currency)];
    }

    public Task<bool> RefundAlreadyRecordedAsync(string idempotencyKey, CancellationToken ct) =>
        db.PaymentTransactions.AnyAsync(
            p => p.Type == PaymentTransactionType.REFUND
                && p.IdempotencyKey == idempotencyKey
                && p.Status == PaymentTransactionStatus.SUCCEEDED, ct);

    private PaymentTransaction Add(PaymentTransaction transaction)
    {
        db.PaymentTransactions.Add(transaction);
        return transaction;
    }

    private static string? Truncate(string? value) =>
        value is { Length: > 500 } ? value[..500] : value;
}
