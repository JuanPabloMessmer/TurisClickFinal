using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TurisClick.Api.Modules.Experiences.Entities;

namespace TurisClick.Api.Modules.Experiences.Persistence;

public class ExperienceImageConfiguration : IEntityTypeConfiguration<ExperienceImage>
{
    public void Configure(EntityTypeBuilder<ExperienceImage> builder)
    {
        builder.ToTable("experience_images");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(i => i.ExperienceId).HasColumnName("experience_id").IsRequired();
        builder.Property(i => i.Url).HasColumnName("url").HasMaxLength(500).IsRequired();
        builder.Property(i => i.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
        builder.Property(i => i.IsCover).HasColumnName("is_cover").HasDefaultValue(false);

        // Mismo motivo que en ExperienceAvailabilityConfiguration: nombre pasado directo en HasIndex(...)
        // para que estos dos índices sobre la misma columna no se pisen entre sí.
        builder.HasIndex(i => i.ExperienceId, "ix_experience_images_experience_id");

        builder.HasIndex(i => i.ExperienceId, "ux_experience_images_one_cover")
            .IsUnique()
            .HasFilter("is_cover = true");
    }
}
