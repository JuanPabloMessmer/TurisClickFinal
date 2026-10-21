using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TurisClick.Api.Modules.Reservations.Entities;

namespace TurisClick.Api.Modules.Reservations.Persistence;

/// <summary>Mapea 1:1 a la tabla `reservation_items` de docs/database-design.md.</summary>
public class ReservationItemConfiguration : IEntityTypeConfiguration<ReservationItem>
{
    public void Configure(EntityTypeBuilder<ReservationItem> builder)
    {
        builder.ToTable("reservation_items", t =>
        {
            t.HasCheckConstraint("ck_reservation_items_travelers", "travelers > 0");
            t.HasCheckConstraint("ck_reservation_items_amounts", "unit_price >= 0 AND subtotal >= 0");
            t.HasCheckConstraint("ck_reservation_items_currency", "currency ~ '^[A-Z]{3}$'");
            t.HasCheckConstraint("ck_reservation_items_cancellation_policy",
                $"cancellation_policy IS NULL OR cancellation_policy ~ '{Policies.CancellationPolicy.ColumnRegex}'");
            // Exactamente un producto por línea, y las columnas de los otros vacías. Oleada 18 suma el vuelo de
            // operador como tercera forma; las dos anteriores pasan a exigir también que no lleven vuelo.
            t.HasCheckConstraint("ck_reservation_items_product_shape",
                "(product_type = 'EXPERIENCE' AND experience_id IS NOT NULL AND package_id IS NULL " +
                "AND experience_availability_id IS NOT NULL AND package_availability_id IS NULL " +
                "AND provider_flight_id IS NULL AND provider_flight_departure_id IS NULL) " +
                "OR (product_type = 'PACKAGE' AND package_id IS NOT NULL AND experience_id IS NULL " +
                "AND package_availability_id IS NOT NULL AND experience_availability_id IS NULL " +
                "AND provider_flight_id IS NULL AND provider_flight_departure_id IS NULL) " +
                "OR (product_type = 'FLIGHT' AND provider_flight_id IS NOT NULL AND provider_flight_departure_id IS NOT NULL " +
                "AND experience_id IS NULL AND package_id IS NULL " +
                "AND experience_availability_id IS NULL AND package_availability_id IS NULL)");
            t.HasCheckConstraint("ck_reservation_items_issuance",
                "issuance_status = 'NOT_REQUIRED' OR product_type = 'FLIGHT'");
            t.HasCheckConstraint("ck_reservation_items_issued_tickets",
                "issuance_status <> 'ISSUED' OR (ticket_numbers IS NOT NULL AND issued_at IS NOT NULL)");
        });

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(i => i.ReservationId).HasColumnName("reservation_id").IsRequired();
        builder.Property(i => i.CompanyId).HasColumnName("company_id").IsRequired();

        builder.Property(i => i.ProductType)
            .HasColumnName("product_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(i => i.ExperienceId).HasColumnName("experience_id");
        builder.Property(i => i.PackageId).HasColumnName("package_id");
        builder.Property(i => i.PackageAvailabilityId).HasColumnName("package_availability_id");
        builder.Property(i => i.ExperienceAvailabilityId).HasColumnName("experience_availability_id");

        builder.Property(i => i.Travelers).HasColumnName("travelers").IsRequired();
        builder.Property(i => i.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(i => i.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(i => i.Subtotal).HasColumnName("subtotal").HasColumnType("numeric(12,2)").IsRequired();

        builder.Property(i => i.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(ReservationItemStatus.PENDING_PAYMENT);

        // UC-P-14 (Oleada 8): trazabilidad de la cancelación a nivel de línea, que es el grano en el que
        // cancela un proveedor. Ambos nulos mientras la línea siga vigente.
        builder.Property(i => i.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(i => i.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(500);

        builder.Property(i => i.DayNumber).HasColumnName("day_number");

        // Snapshot de la política del producto, igual que unit_price congela su precio.
        builder.Property(i => i.CancellationPolicy)
            .HasColumnName("cancellation_policy")
            .HasMaxLength(TurisClick.Api.Modules.Reservations.Policies.CancellationPolicy.MaxLength);
        builder.Property(i => i.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        // Vuelo de operador (Oleada 18).
        builder.Property(i => i.ProviderFlightId).HasColumnName("provider_flight_id");
        builder.Property(i => i.ProviderFlightDepartureId).HasColumnName("provider_flight_departure_id");
        builder.Property(i => i.IssuanceStatus)
            .HasColumnName("issuance_status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(FlightIssuanceStatus.NOT_REQUIRED);
        builder.Property(i => i.TicketNumbers).HasColumnName("ticket_numbers").HasMaxLength(500);
        builder.Property(i => i.BookingReference).HasColumnName("booking_reference").HasMaxLength(20);
        builder.Property(i => i.IssuedAt).HasColumnName("issued_at");
        builder.Property(i => i.IssuedByUserId).HasColumnName("issued_by");
        builder.Property(i => i.IssuanceNote).HasColumnName("issuance_note").HasMaxLength(500);

        builder.HasIndex(i => i.ProviderFlightDepartureId).HasDatabaseName("ix_reservation_items_provider_flight_departure");
        // La alerta del ADMIN ("pagado sin emitir") busca por este par.
        builder.HasIndex(i => new { i.IssuanceStatus, i.ProductType }).HasDatabaseName("ix_reservation_items_issuance");

        builder.HasOne(i => i.ProviderFlight)
            .WithMany()
            .HasForeignKey(i => i.ProviderFlightId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.ProviderFlightDeparture)
            .WithMany()
            .HasForeignKey(i => i.ProviderFlightDepartureId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => i.ReservationId).HasDatabaseName("ix_reservation_items_reservation_id");
        builder.HasIndex(i => new { i.CompanyId, i.Status }, "ix_reservation_items_company_status");
        builder.HasIndex(i => i.ExperienceAvailabilityId).HasDatabaseName("ix_reservation_items_experience_availability");
        builder.HasIndex(i => i.PackageAvailabilityId).HasDatabaseName("ix_reservation_items_package_availability");

        builder.HasOne(i => i.Company)
            .WithMany()
            .HasForeignKey(i => i.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Experience)
            .WithMany()
            .HasForeignKey(i => i.ExperienceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.ExperienceAvailability)
            .WithMany()
            .HasForeignKey(i => i.ExperienceAvailabilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Package)
            .WithMany()
            .HasForeignKey(i => i.PackageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.PackageAvailability)
            .WithMany()
            .HasForeignKey(i => i.PackageAvailabilityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
