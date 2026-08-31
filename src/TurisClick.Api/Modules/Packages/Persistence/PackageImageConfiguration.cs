using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TurisClick.Api.Modules.Packages.Entities;

namespace TurisClick.Api.Modules.Packages.Persistence;

public class PackageImageConfiguration : IEntityTypeConfiguration<PackageImage>
{
    public void Configure(EntityTypeBuilder<PackageImage> builder)
    {
        builder.ToTable("package_images");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(i => i.PackageId).HasColumnName("package_id").IsRequired();
        builder.Property(i => i.Url).HasColumnName("url").HasMaxLength(500).IsRequired();
        builder.Property(i => i.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
        builder.Property(i => i.IsCover).HasColumnName("is_cover").HasDefaultValue(false);

        // Nombre pasado directo en HasIndex(...) — mismo motivo que ExperienceImageConfiguration:
        // así estos dos índices sobre la misma columna no se pisan entre sí.
        builder.HasIndex(i => i.PackageId, "ix_package_images_package_id");

        builder.HasIndex(i => i.PackageId, "ux_package_images_one_cover")
            .IsUnique()
            .HasFilter("is_cover = true");
    }
}
