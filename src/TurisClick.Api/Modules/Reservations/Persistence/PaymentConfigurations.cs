using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TurisClick.Api.Modules.Reservations.Entities;
using TurisClick.Api.Modules.Reservations.Policies;

namespace TurisClick.Api.Modules.Reservations.Persistence;

/// <summary>
/// Libro de movimientos de dinero. Todo acá empuja en la misma dirección: que una fila escrita no se pueda
/// reescribir y que un reembolso no se pueda duplicar.
/// </summary>
public class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.ToTable("payment_transactions", t =>
        {
            t.HasCheckConstraint("ck_payment_transactions_amount", "amount >= 0");
            t.HasCheckConstraint("ck_payment_transactions_currency", "currency ~ '^[A-Z]{3}$'");
            // Un cobro es de la reserva entera en una moneda; un reembolso siempre nace de un componente.
            // La base lo exige para que no entre un reembolso sin saber de dónde salió.
            t.HasCheckConstraint("ck_payment_transactions_component",
                "type <> 'REFUND' OR component IS NOT NULL");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(p => p.ReservationId).HasColumnName("reservation_id").IsRequired();

        builder.Property(p => p.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(p => p.Amount).HasColumnName("amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(p => p.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();

        builder.Property(p => p.Component).HasColumnName("component").HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.ReservationItemId).HasColumnName("reservation_item_id");

        builder.Property(p => p.Provider).HasColumnName("provider").HasMaxLength(50).IsRequired();
        builder.Property(p => p.ProviderReference).HasColumnName("provider_reference").HasMaxLength(200);
        builder.Property(p => p.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(120).IsRequired();
        builder.Property(p => p.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);

        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        builder.HasIndex(p => new { p.ReservationId, p.CreatedAt }).HasDatabaseName("ix_payment_transactions_reservation");

        // Un reembolso se concreta UNA vez. Lo único de lo que la base es garante es de eso: que no haya dos
        // reembolsos **exitosos** con la misma clave, pase lo que pase con la concurrencia.
        //
        // El filtro incluye el estado a propósito. Un intento FALLIDO tiene que poder guardarse —es la única
        // forma de saber después qué operación falló— y el reintento posterior tiene que poder guardarse
        // también. Un índice sobre `type = 'REFUND'` a secas prohibía las dos cosas: convertía un reembolso
        // rechazado en un reembolso imposible de reintentar.
        builder.HasIndex(p => p.IdempotencyKey)
            .IsUnique()
            .HasFilter("type = 'REFUND' AND status = 'SUCCEEDED'")
            .HasDatabaseName("ux_payment_transactions_refund_key");

        builder.HasOne(p => p.Reservation)
            .WithMany()
            .HasForeignKey(p => p.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ReservationCancellationConfiguration : IEntityTypeConfiguration<ReservationCancellation>
{
    public void Configure(EntityTypeBuilder<ReservationCancellation> builder)
    {
        builder.ToTable("reservation_cancellations");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(c => c.ReservationId).HasColumnName("reservation_id").IsRequired();
        builder.Property(c => c.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(c => c.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(c => c.ProviderCancellationId).HasColumnName("provider_cancellation_id").HasMaxLength(200);

        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(c => c.AcceptedAt).HasColumnName("accepted_at");
        builder.Property(c => c.CompletedAt).HasColumnName("completed_at");
        builder.Property(c => c.FlightCancelledAt).HasColumnName("flight_cancelled_at");
        builder.Property(c => c.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);

        builder.Property(c => c.ResolutionAttempts).HasColumnName("resolution_attempts").IsRequired();
        builder.Property(c => c.NextResolutionAt).HasColumnName("next_resolution_at");

        builder.HasIndex(c => new { c.ReservationId, c.CreatedAt }).HasDatabaseName("ix_reservation_cancellations_reservation");

        // Lo que lee el proceso que resuelve lo que quedó a medias: pocas filas, y sólo las que importan.
        builder.HasIndex(c => new { c.Status, c.NextResolutionAt }).HasDatabaseName("ix_reservation_cancellations_pending");

        builder.HasOne(c => c.Reservation)
            .WithMany()
            .HasForeignKey(c => c.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Lines)
            .WithOne(l => l.Cancellation)
            .HasForeignKey(l => l.CancellationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ReservationCancellationLineConfiguration : IEntityTypeConfiguration<ReservationCancellationLine>
{
    public void Configure(EntityTypeBuilder<ReservationCancellationLine> builder)
    {
        builder.ToTable("reservation_cancellation_lines", t =>
        {
            t.HasCheckConstraint("ck_cancellation_lines_amounts",
                "paid_amount >= 0 AND refund_amount >= 0 AND fee_amount >= 0 AND refund_amount <= paid_amount");
            t.HasCheckConstraint("ck_cancellation_lines_currency", "currency ~ '^[A-Z]{3}$'");
            t.HasCheckConstraint("ck_cancellation_lines_percentage",
                "refund_percentage IS NULL OR (refund_percentage BETWEEN 0 AND 100)");
        });

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(l => l.CancellationId).HasColumnName("cancellation_id").IsRequired();
        builder.Property(l => l.Component).HasColumnName("component").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(l => l.ReservationItemId).HasColumnName("reservation_item_id");

        builder.Property(l => l.Label).HasColumnName("label").HasMaxLength(200).IsRequired();

        builder.Property(l => l.PaidAmount).HasColumnName("paid_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(l => l.RefundAmount).HasColumnName("refund_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(l => l.FeeAmount).HasColumnName("fee_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(l => l.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();

        builder.Property(l => l.PolicyApplied)
            .HasColumnName("policy_applied")
            .HasMaxLength(CancellationPolicy.MaxLength);

        builder.Property(l => l.RefundPercentage).HasColumnName("refund_percentage");
        builder.Property(l => l.RefundKnown).HasColumnName("refund_known").IsRequired();
        builder.Property(l => l.Explanation).HasColumnName("explanation").HasMaxLength(300).IsRequired();
    }
}
