using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TurisClick.Api.Modules.Ai.Entities;
using TurisClick.Api.Modules.Categories.Entities;

namespace TurisClick.Api.Modules.Ai.Persistence;

/// <summary>Mapea 1:1 a la tabla `ai_conversations` de docs/database-design.md.</summary>
public class AiConversationConfiguration : IEntityTypeConfiguration<AiConversation>
{
    public void Configure(EntityTypeBuilder<AiConversation> builder)
    {
        builder.ToTable("ai_conversations", t =>
        {
            t.HasCheckConstraint("ck_ai_conversations_dates", "end_date IS NULL OR start_date IS NULL OR end_date >= start_date");
            t.HasCheckConstraint("ck_ai_conversations_travelers", "travelers_count IS NULL OR travelers_count > 0");
            t.HasCheckConstraint("ck_ai_conversations_budget", "budget_total IS NULL OR budget_total >= 0");
            t.HasCheckConstraint("ck_ai_conversations_currency", "budget_currency IS NULL OR budget_currency ~ '^[A-Z]{3}$'");
        });

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(c => c.TouristId).HasColumnName("tourist_id").IsRequired();

        builder.Property(c => c.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(AiConversationStatus.ACTIVE);

        builder.Property(c => c.PreferredDestinationId).HasColumnName("preferred_destination_id");
        builder.Property(c => c.StartDate).HasColumnName("start_date").HasColumnType("date");
        builder.Property(c => c.EndDate).HasColumnName("end_date").HasColumnType("date");
        builder.Property(c => c.TravelersCount).HasColumnName("travelers_count");
        builder.Property(c => c.BudgetTotal).HasColumnName("budget_total").HasColumnType("numeric(12,2)");
        builder.Property(c => c.BudgetCurrency).HasColumnName("budget_currency").HasMaxLength(3);
        builder.Property(c => c.DurationDays).HasColumnName("duration_days");
        builder.Property(c => c.RestrictionsNotes).HasColumnName("restrictions_notes");

        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.HasIndex(c => c.TouristId).HasDatabaseName("ix_ai_conversations_tourist_id");

        builder.HasOne(c => c.Tourist)
            .WithMany()
            .HasForeignKey(c => c.TouristId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.PreferredDestination)
            .WithMany()
            .HasForeignKey(c => c.PreferredDestinationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Messages)
            .WithOne(m => m.AiConversation)
            .HasForeignKey(m => m.AiConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Itineraries)
            .WithOne(i => i.AiConversation)
            .HasForeignKey(i => i.AiConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(c => c.TravelPace)
            .HasColumnName("travel_pace")
            .HasConversion<string>()
            .HasMaxLength(20);

        // N—N con Category, sin navegación inversa (mismo patrón que Experience/Package).
        builder.HasMany(c => c.Categories)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "ai_conversation_categories",
                j => j.HasOne<Category>().WithMany().HasForeignKey("category_id"),
                j => j.HasOne<AiConversation>().WithMany().HasForeignKey("ai_conversation_id"),
                j =>
                {
                    j.ToTable("ai_conversation_categories");
                    j.HasKey("ai_conversation_id", "category_id");
                });
    }
}
