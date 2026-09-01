using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TurisClick.Api.Modules.Ai.Entities;

namespace TurisClick.Api.Modules.Ai.Persistence;

/// <summary>Mapea 1:1 a la tabla `ai_itineraries` de docs/database-design.md.</summary>
public class AiItineraryConfiguration : IEntityTypeConfiguration<AiItinerary>
{
    public void Configure(EntityTypeBuilder<AiItinerary> builder)
    {
        builder.ToTable("ai_itineraries");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(i => i.AiConversationId).HasColumnName("ai_conversation_id").IsRequired();
        builder.Property(i => i.TouristId).HasColumnName("tourist_id").IsRequired();
        builder.Property(i => i.Title).HasColumnName("title").HasMaxLength(200);

        builder.Property(i => i.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(AiItineraryStatus.DRAFT);

        builder.Property(i => i.Version).HasColumnName("version").HasDefaultValue(1);
        builder.Property(i => i.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.HasIndex(i => i.TouristId).HasDatabaseName("ix_ai_itineraries_tourist_id");
        builder.HasIndex(i => i.AiConversationId).HasDatabaseName("ix_ai_itineraries_conversation_id");
        builder.HasIndex(i => i.Status).HasDatabaseName("ix_ai_itineraries_status");

        builder.HasOne(i => i.Tourist)
            .WithMany()
            .HasForeignKey(i => i.TouristId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.Items)
            .WithOne(it => it.AiItinerary)
            .HasForeignKey(it => it.AiItineraryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
