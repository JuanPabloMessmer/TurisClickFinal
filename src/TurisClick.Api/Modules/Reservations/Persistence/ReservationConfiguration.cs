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
        // ÚNICO y parcial: domain-model.md documenta AiItinerary 1—1 opcional Reservation, pero hasta
        // Oleada 6 el schema solo tenía un índice común y permitía N. La unicidad es lo que hace
        // realmente idempotente al booking (UC-T-18): dos requests concurrentes pueden leer el
        // itinerario como reservable al mismo tiempo, y un chequeo en C# no alcanza — acá pierde uno.
        // El filtro deja fuera las reservas directas (ai_itinerary_id NULL), que no se ven afectadas.
        //
        // Oleada 8: el invariante pasa de "una reserva histórica" a "una reserva ACTIVA" por itinerario,
        // para que un itinerario cuya reserva expiró se pueda volver a reservar conservando la reserva
        // vieja como auditoría. Se excluyen los estados TERMINALES (los que ya liberaron el cupo) en vez
        // de listar los activos: así cualquier estado que se agregue en el futuro bloquea por defecto,
        // que es el lado seguro del error. PAYMENT_FAILED queda dentro de "activo" a propósito — hoy
        // ningún código lo escribe, pero según UC-T-19 ahí el cupo sigue retenido hasta expirar.
        builder.HasIndex(r => r.AiItineraryId)
            .HasDatabaseName("ix_reservations_ai_itinerary_id")
            .IsUnique()
            .HasFilter("ai_itinerary_id IS NOT NULL AND status NOT IN ('EXPIRED', 'CANCELLED')");

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
