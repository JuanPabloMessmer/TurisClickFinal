using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TurisClick.Api.Modules.Reservations.Entities;

namespace TurisClick.Api.Modules.Reservations.Persistence;

/// <summary>Mapea 1:1 a la tabla `reservations` de docs/database-design.md.</summary>
public class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("reservations");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(r => r.TouristId).HasColumnName("tourist_id").IsRequired();

        // Sin FK física: ai_itineraries no existe hasta Oleada 5+ (ver Reservation.cs).
        builder.Property(r => r.AiItineraryId).HasColumnName("ai_itinerary_id");

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(ReservationStatus.PENDING_PAYMENT);

        builder.Property(r => r.ExpiresAt).HasColumnName("expires_at");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(r => r.ConfirmedAt).HasColumnName("confirmed_at");
        builder.Property(r => r.CancelledAt).HasColumnName("cancelled_at");

        builder.HasIndex(r => r.TouristId).HasDatabaseName("ix_reservations_tourist_id");
        builder.HasIndex(r => r.Status).HasDatabaseName("ix_reservations_status");
        builder.HasIndex(r => r.AiItineraryId).HasDatabaseName("ix_reservations_ai_itinerary_id");

        builder.HasOne(r => r.Tourist)
            .WithMany()
            .HasForeignKey(r => r.TouristId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Items)
            .WithOne(i => i.Reservation)
            .HasForeignKey(i => i.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
