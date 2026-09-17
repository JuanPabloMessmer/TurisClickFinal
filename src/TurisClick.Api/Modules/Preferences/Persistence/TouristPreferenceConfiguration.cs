using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TurisClick.Api.Modules.Auth.Entities;
using TurisClick.Api.Modules.Categories.Entities;
using TurisClick.Api.Modules.Preferences.Entities;

namespace TurisClick.Api.Modules.Preferences.Persistence;

public class TouristPreferenceConfiguration : IEntityTypeConfiguration<TouristPreference>
{
    public void Configure(EntityTypeBuilder<TouristPreference> builder)
    {
        builder.ToTable("tourist_preferences");

        builder.HasKey(p => p.UserId);
        builder.Property(p => p.UserId).HasColumnName("user_id");

        builder.HasOne(p => p.User)
            .WithOne()
            .HasForeignKey<TouristPreference>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(p => p.TravelPace).HasColumnName("travel_pace").HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.TravelParty).HasColumnName("travel_party").HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.BudgetLevel).HasColumnName("budget_level").HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.OnboardingCompletedAt).HasColumnName("onboarding_completed_at");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        // N—N con Category, mismo patrón que AiConversation. Borrar una categoría solo quita el interés.
        builder.HasMany(p => p.Categories)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "tourist_preference_categories",
                j => j.HasOne<Category>().WithMany().HasForeignKey("category_id").OnDelete(DeleteBehavior.Cascade),
                j => j.HasOne<TouristPreference>().WithMany().HasForeignKey("user_id").OnDelete(DeleteBehavior.Cascade),
                j =>
                {
                    j.ToTable("tourist_preference_categories");
                    j.HasKey("user_id", "category_id");
                    j.HasIndex("category_id").HasDatabaseName("ix_tourist_preference_categories_category_id");
                });
    }
}
