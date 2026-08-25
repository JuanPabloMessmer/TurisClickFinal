using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TurisClick.Api.Modules.Categories.Entities;
using TurisClick.Api.Modules.Experiences.Entities;

namespace TurisClick.Api.Modules.Experiences.Persistence;

/// <summary>Mapea 1:1 a la tabla `experiences` de docs/database-design.md.</summary>
public class ExperienceConfiguration : IEntityTypeConfiguration<Experience>
{
    public void Configure(EntityTypeBuilder<Experience> builder)
    {
        builder.ToTable("experiences", t =>
        {
            t.HasCheckConstraint("ck_experiences_price", "price >= 0");
            t.HasCheckConstraint("ck_experiences_duration", "duration_minutes IS NULL OR duration_minutes > 0");
            t.HasCheckConstraint("ck_experiences_currency", "currency ~ '^[A-Z]{3}$'");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(e => e.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(e => e.DestinationId).HasColumnName("destination_id").IsRequired();

        builder.Property(e => e.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasColumnName("description").IsRequired();
        builder.Property(e => e.IncludesText).HasColumnName("includes_text");
        builder.Property(e => e.ExcludesText).HasColumnName("excludes_text");
        builder.Property(e => e.DurationMinutes).HasColumnName("duration_minutes");
        builder.Property(e => e.DurationLabel).HasColumnName("duration_label").HasMaxLength(100);
        builder.Property(e => e.Price).HasColumnName("price").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(e => e.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(PublicationStatus.DRAFT);

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.HasIndex(e => e.CompanyId).HasDatabaseName("ix_experiences_company_id");
        builder.HasIndex(e => e.DestinationId).HasDatabaseName("ix_experiences_destination_id");
        builder.HasIndex(e => new { e.Status, e.DestinationId }).HasDatabaseName("ix_experiences_status_destination");

        // Company/Destination: sin cascada — un producto no debería desaparecer si se toca su empresa/destino.
        builder.HasOne(e => e.Company)
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Destination)
            .WithMany()
            .HasForeignKey(e => e.DestinationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Images)
            .WithOne(i => i.Experience)
            .HasForeignKey(i => i.ExperienceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Availabilities)
            .WithOne(a => a.Experience)
            .HasForeignKey(a => a.ExperienceId)
            .OnDelete(DeleteBehavior.Cascade);

        // N—N con Category, sin navegación inversa en Category (nadie necesita "categorías -> sus experiencias" todavía).
        builder.HasMany(e => e.Categories)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "experience_categories",
                j => j.HasOne<Category>().WithMany().HasForeignKey("category_id"),
                j => j.HasOne<Experience>().WithMany().HasForeignKey("experience_id"),
                j =>
                {
                    j.ToTable("experience_categories");
                    j.HasKey("experience_id", "category_id");
                    j.HasIndex("category_id").HasDatabaseName("ix_experience_categories_category_id");
                });
    }
}
