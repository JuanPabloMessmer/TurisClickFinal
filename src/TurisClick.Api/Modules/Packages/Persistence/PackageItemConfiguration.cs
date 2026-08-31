using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Packages.Entities;

namespace TurisClick.Api.Modules.Packages.Persistence;

/// <summary>Mapea 1:1 a la tabla `package_items` de docs/database-design.md.</summary>
public class PackageItemConfiguration : IEntityTypeConfiguration<PackageItem>
{
    public void Configure(EntityTypeBuilder<PackageItem> builder)
    {
        builder.ToTable("package_items", t =>
        {
            t.HasCheckConstraint("ck_package_items_day", "day_number >= 1");
            t.HasCheckConstraint("ck_package_items_kind_shape",
                "(kind = 'EXPERIENCE_REFERENCE' AND experience_id IS NOT NULL) " +
                "OR (kind = 'DESCRIPTIVE' AND experience_id IS NULL)");
            t.HasCheckConstraint("ck_package_items_descriptive_title", "kind <> 'DESCRIPTIVE' OR title IS NOT NULL");
        });

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(i => i.PackageId).HasColumnName("package_id").IsRequired();
        builder.Property(i => i.DayNumber).HasColumnName("day_number").IsRequired();
        builder.Property(i => i.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);

        builder.Property(i => i.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(i => i.ExperienceId).HasColumnName("experience_id");
        builder.Property(i => i.Title).HasColumnName("title").HasMaxLength(200);
        builder.Property(i => i.Description).HasColumnName("description");

        builder.HasIndex(i => i.PackageId).HasDatabaseName("ix_package_items_package_id");
        builder.HasIndex(i => i.ExperienceId).HasDatabaseName("ix_package_items_experience_id");

        // Invariante NO expresable aquí: experience_id.company_id debe ser igual a package_id.company_id.
        // Se valida en PackageService al crear/editar un PackageItem (UC-P-07/08) — ver database-design.md.
        builder.HasOne(i => i.Experience)
            .WithMany()
            .HasForeignKey(i => i.ExperienceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
