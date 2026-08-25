using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TurisClick.Api.Modules.Experiences.Entities;

namespace TurisClick.Api.Modules.Experiences.Persistence;

public class ExperienceAvailabilityConfiguration : IEntityTypeConfiguration<ExperienceAvailability>
{
    public void Configure(EntityTypeBuilder<ExperienceAvailability> builder)
    {
        builder.ToTable("experience_availabilities", t => t.HasCheckConstraint(
            "ck_experience_availabilities_slots",
            "total_slots > 0 AND reserved_slots >= 0 AND reserved_slots <= total_slots"));

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(a => a.ExperienceId).HasColumnName("experience_id").IsRequired();
        builder.Property(a => a.Date).HasColumnName("date").HasColumnType("date").IsRequired();
        builder.Property(a => a.StartTime).HasColumnName("start_time").HasColumnType("time");
        builder.Property(a => a.TotalSlots).HasColumnName("total_slots").IsRequired();
        builder.Property(a => a.ReservedSlots).HasColumnName("reserved_slots").HasDefaultValue(0);

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(AvailabilitySlotStatus.OPEN);

        builder.Ignore(a => a.AvailableSlots);

        // Los tres índices comparten (experience_id, date) como prefijo — EF Core solo los mantiene
        // separados si el nombre se pasa directo en HasIndex(...); pasarlo después vía HasDatabaseName()
        // hace que el segundo HasIndex con las mismas columnas reconfigure el primero en vez de crear uno nuevo.
        builder.HasIndex(a => new { a.ExperienceId, a.Date }, "ix_experience_availabilities_experience_date");

        builder.HasIndex(a => new { a.ExperienceId, a.Date, a.StartTime }, "ux_experience_availabilities_timed")
            .IsUnique()
            .HasFilter("start_time IS NOT NULL");

        builder.HasIndex(a => new { a.ExperienceId, a.Date }, "ux_experience_availabilities_fullday")
            .IsUnique()
            .HasFilter("start_time IS NULL");
    }
}
