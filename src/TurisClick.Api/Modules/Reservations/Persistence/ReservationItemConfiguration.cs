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
            t.HasCheckConstraint("ck_reservation_items_product_shape",
                "(product_type = 'EXPERIENCE' AND experience_id IS NOT NULL AND package_id IS NULL " +
                "AND experience_availability_id IS NOT NULL AND package_availability_id IS NULL) " +
                "OR (product_type = 'PACKAGE' AND package_id IS NOT NULL AND experience_id IS NULL " +
                "AND package_availability_id IS NOT NULL AND experience_availability_id IS NULL)");
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
        builder.Property(i => i.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

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
