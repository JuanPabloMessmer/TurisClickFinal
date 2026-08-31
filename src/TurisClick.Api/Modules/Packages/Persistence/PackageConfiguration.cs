using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TurisClick.Api.Modules.Categories.Entities;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Packages.Entities;

namespace TurisClick.Api.Modules.Packages.Persistence;

/// <summary>Mapea 1:1 a la tabla `packages` de docs/database-design.md.</summary>
public class PackageConfiguration : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> builder)
    {
        builder.ToTable("packages", t =>
        {
            t.HasCheckConstraint("ck_packages_duration", "duration_days > 0");
            t.HasCheckConstraint("ck_packages_price", "price >= 0");
            t.HasCheckConstraint("ck_packages_currency", "currency ~ '^[A-Z]{3}$'");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(p => p.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(p => p.DestinationId).HasColumnName("destination_id").IsRequired();

        builder.Property(p => p.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasColumnName("description").IsRequired();
        builder.Property(p => p.ConditionsText).HasColumnName("conditions_text");
        builder.Property(p => p.DurationDays).HasColumnName("duration_days").IsRequired();
        builder.Property(p => p.Price).HasColumnName("price").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(p => p.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();

        builder.Property(p => p.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(PublicationStatus.DRAFT);

        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.HasIndex(p => p.CompanyId).HasDatabaseName("ix_packages_company_id");
        builder.HasIndex(p => p.DestinationId).HasDatabaseName("ix_packages_destination_id");
        builder.HasIndex(p => new { p.Status, p.DestinationId }).HasDatabaseName("ix_packages_status_destination");

        // Company/Destination: sin cascada — un producto no debería desaparecer si se toca su empresa/destino.
        builder.HasOne(p => p.Company)
            .WithMany()
            .HasForeignKey(p => p.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Destination)
            .WithMany()
            .HasForeignKey(p => p.DestinationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Items)
            .WithOne(i => i.Package)
            .HasForeignKey(i => i.PackageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Images)
            .WithOne(i => i.Package)
            .HasForeignKey(i => i.PackageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Availabilities)
            .WithOne(a => a.Package)
            .HasForeignKey(a => a.PackageId)
            .OnDelete(DeleteBehavior.Cascade);

        // N—N con Category, sin navegación inversa en Category (mismo patrón que Experience).
        builder.HasMany(p => p.Categories)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "package_categories",
                j => j.HasOne<Category>().WithMany().HasForeignKey("category_id"),
                j => j.HasOne<Package>().WithMany().HasForeignKey("package_id"),
                j =>
                {
                    j.ToTable("package_categories");
                    j.HasKey("package_id", "category_id");
                    j.HasIndex("category_id").HasDatabaseName("ix_package_categories_category_id");
                });
    }
}
