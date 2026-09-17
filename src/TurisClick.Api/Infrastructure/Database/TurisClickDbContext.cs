using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Modules.Ai.Entities;
using TurisClick.Api.Modules.Auth.Entities;
using TurisClick.Api.Modules.Categories.Entities;
using TurisClick.Api.Modules.Companies.Entities;
using TurisClick.Api.Modules.Destinations.Entities;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Packages.Entities;
using TurisClick.Api.Modules.Preferences.Entities;
using TurisClick.Api.Modules.Reservations.Entities;

namespace TurisClick.Api.Infrastructure.Database;

/// <summary>
/// Un solo DbContext para todo el proyecto (ver docs/backend-architecture.md, punto 11).
/// Cada módulo aporta sus IEntityTypeConfiguration; este contexto solo las descubre y expone los DbSet.
/// </summary>
public class TurisClickDbContext(DbContextOptions<TurisClickDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Destination> Destinations => Set<Destination>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Experience> Experiences => Set<Experience>();
    public DbSet<ExperienceImage> ExperienceImages => Set<ExperienceImage>();
    public DbSet<ExperienceAvailability> ExperienceAvailabilities => Set<ExperienceAvailability>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<PackageItem> PackageItems => Set<PackageItem>();
    public DbSet<PackageImage> PackageImages => Set<PackageImage>();
    public DbSet<PackageAvailability> PackageAvailabilities => Set<PackageAvailability>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<ReservationItem> ReservationItems => Set<ReservationItem>();
    public DbSet<AiConversation> AiConversations => Set<AiConversation>();
    public DbSet<AiMessage> AiMessages => Set<AiMessage>();
    public DbSet<AiItinerary> AiItineraries => Set<AiItinerary>();
    public DbSet<AiItineraryItem> AiItineraryItems => Set<AiItineraryItem>();
    public DbSet<TouristPreference> TouristPreferences => Set<TouristPreference>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TurisClickDbContext).Assembly);

        // FK diferida: reservations.ai_itinerary_id → ai_itineraries.id. ai_itineraries no existía hasta
        // esta oleada (mismo patrón que companies↔users y reservation_items↔packages en oleadas
        // anteriores) — se agrega acá, fuera de ReservationConfiguration, para no tocar el módulo de
        // Reservations por una tabla que pertenece a Ai. Sin navegación C# todavía: UC-T-18/UC-SYS-05
        // (que la usarían) son Oleada 7.
        modelBuilder.Entity<Reservation>()
            .HasOne<AiItinerary>()
            .WithMany()
            .HasForeignKey(r => r.AiItineraryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
