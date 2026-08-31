using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Packages.Entities;

namespace TurisClick.Api.Modules.Packages.Persistence;

public class PackageAvailabilityConfiguration : IEntityTypeConfiguration<PackageAvailability>
{
    public void Configure(EntityTypeBuilder<PackageAvailability> builder)
    {
        builder.ToTable("package_availabilities", t => t.HasCheckConstraint(
            "ck_package_availabilities_slots",
            "total_slots > 0 AND reserved_slots >= 0 AND reserved_slots <= total_slots"));

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(a => a.PackageId).HasColumnName("package_id").IsRequired();
        builder.Property(a => a.DepartureDate).HasColumnName("departure_date").HasColumnType("date").IsRequired();
        builder.Property(a => a.TotalSlots).HasColumnName("total_slots").IsRequired();
        builder.Property(a => a.ReservedSlots).HasColumnName("reserved_slots").HasDefaultValue(0);

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(AvailabilitySlotStatus.OPEN);

        builder.Ignore(a => a.AvailableSlots);

        builder.HasIndex(a => new { a.PackageId, a.DepartureDate }, "ix_package_availabilities_package_date");

        builder.HasIndex(a => new { a.PackageId, a.DepartureDate }, "uq_package_availabilities_departure")
            .IsUnique();
    }
}
