using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TurisClick.Api.Modules.Ai.Entities;

namespace TurisClick.Api.Modules.Ai.Persistence;

/// <summary>Mapea 1:1 a la tabla `ai_itinerary_items` de docs/database-design.md.</summary>
public class AiItineraryItemConfiguration : IEntityTypeConfiguration<AiItineraryItem>
{
    public void Configure(EntityTypeBuilder<AiItineraryItem> builder)
    {
        builder.ToTable("ai_itinerary_items", t =>
        {
            t.HasCheckConstraint("ck_ai_itinerary_items_day", "day_number >= 1");
            // A diferencia de reservation_items, acá EXPERIENCE no exige experience_availability_id
            // NOT NULL — la propuesta puede razonar todavía en términos de "Día N" sin slot calendario
            // fijado (ver domain-model.md §8).
            t.HasCheckConstraint("ck_ai_itinerary_items_product_shape",
                "(product_type = 'EXPERIENCE' AND experience_id IS NOT NULL AND package_id IS NULL " +
                "AND package_availability_id IS NULL) " +
                "OR (product_type = 'PACKAGE' AND package_id IS NOT NULL AND experience_id IS NULL " +
                "AND experience_availability_id IS NULL)");
            t.HasCheckConstraint("ck_ai_itinerary_items_currency", "currency ~ '^[A-Z]{3}$'");
        });

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(i => i.AiItineraryId).HasColumnName("ai_itinerary_id").IsRequired();
        builder.Property(i => i.DayNumber).HasColumnName("day_number").IsRequired();
        builder.Property(i => i.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);

        builder.Property(i => i.ProductType)
            .HasColumnName("product_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(i => i.ExperienceId).HasColumnName("experience_id");
        builder.Property(i => i.PackageId).HasColumnName("package_id");
        builder.Property(i => i.ExperienceAvailabilityId).HasColumnName("experience_availability_id");
        builder.Property(i => i.PackageAvailabilityId).HasColumnName("package_availability_id");

        builder.Property(i => i.EstimatedUnitPrice).HasColumnName("estimated_unit_price").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(i => i.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();

        builder.HasIndex(i => i.AiItineraryId).HasDatabaseName("ix_ai_itinerary_items_itinerary_id");

        builder.HasOne(i => i.Experience)
            .WithMany()
            .HasForeignKey(i => i.ExperienceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Package)
            .WithMany()
            .HasForeignKey(i => i.PackageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.ExperienceAvailability)
            .WithMany()
            .HasForeignKey(i => i.ExperienceAvailabilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.PackageAvailability)
            .WithMany()
            .HasForeignKey(i => i.PackageAvailabilityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
