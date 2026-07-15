using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Modules.Categories;
using TurisClick.Api.Modules.Destinations;
using TurisClick.Api.Modules.Providers;
using TurisClick.Api.Modules.Roles;
using TurisClick.Api.Modules.Users;

namespace TurisClick.Api.Infrastructure.Database;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Provider> Providers => Set<Provider>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Destination> Destinations => Set<Destination>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureRoles(modelBuilder);
        ConfigureUsers(modelBuilder);
        ConfigureProviders(modelBuilder);
        ConfigureCategories(modelBuilder);
        ConfigureDestinations(modelBuilder);
    }

    private static void ConfigureRoles(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("roles");

            entity.HasKey(role => role.RoleId);

            entity.Property(role => role.RoleId)
                .HasColumnName("role_id");

            entity.Property(role => role.Name)
                .HasColumnName("name")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(role => role.Description)
                .HasColumnName("description")
                .HasMaxLength(255);

            entity.HasIndex(role => role.Name)
                .IsUnique();

            entity.HasData(
                new Role { RoleId = 1, Name = "ADMIN", Description = "System administrator" },
                new Role { RoleId = 2, Name = "TOURIST", Description = "Tourist platform user" },
                new Role { RoleId = 3, Name = "PROVIDER", Description = "Tourism provider platform user" }
            );
        });
    }

    private static void ConfigureUsers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");

            entity.HasKey(user => user.UserId);

            entity.Property(user => user.UserId)
                .HasColumnName("user_id");

            entity.Property(user => user.RoleId)
                .HasColumnName("role_id")
                .IsRequired();

            entity.Property(user => user.FirstName)
                .HasColumnName("first_name")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(user => user.LastName)
                .HasColumnName("last_name")
                .HasMaxLength(100);

            entity.Property(user => user.Email)
                .HasColumnName("email")
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(user => user.PasswordHash)
                .HasColumnName("password_hash")
                .HasMaxLength(255)
                .IsRequired();

            entity.Property(user => user.Phone)
                .HasColumnName("phone")
                .HasMaxLength(30);

            entity.Property(user => user.Status)
                .HasColumnName("status")
                .HasMaxLength(20)
                .HasDefaultValue("ACTIVE")
                .IsRequired();

            entity.Property(user => user.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("now()");

            entity.HasIndex(user => user.Email)
                .IsUnique();

            entity.HasOne(user => user.Role)
                .WithMany(role => role.Users)
                .HasForeignKey(user => user.RoleId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureProviders(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Provider>(entity =>
        {
            entity.ToTable("providers");

            entity.HasKey(provider => provider.ProviderId);

            entity.Property(provider => provider.ProviderId)
                .HasColumnName("provider_id");

            entity.Property(provider => provider.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            entity.Property(provider => provider.CompanyName)
                .HasColumnName("company_name")
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(provider => provider.Nit)
                .HasColumnName("nit")
                .HasMaxLength(50);

            entity.Property(provider => provider.Description)
                .HasColumnName("description")
                .HasColumnType("text");

            entity.Property(provider => provider.ContactPhone)
                .HasColumnName("contact_phone")
                .HasMaxLength(30);

            entity.Property(provider => provider.ContactEmail)
                .HasColumnName("contact_email")
                .HasMaxLength(150);

            entity.Property(provider => provider.Address)
                .HasColumnName("address")
                .HasMaxLength(255);

            entity.Property(provider => provider.LogoUrl)
                .HasColumnName("logo_url")
                .HasMaxLength(500);

            entity.Property(provider => provider.Status)
                .HasColumnName("status")
                .HasMaxLength(20)
                .HasDefaultValue("ACTIVE")
                .IsRequired();

            entity.Property(provider => provider.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("now()");

            entity.HasIndex(provider => provider.UserId)
                .IsUnique();

            entity.HasIndex(provider => provider.Nit)
                .IsUnique();

            entity.HasOne(provider => provider.User)
                .WithOne(user => user.Provider)
                .HasForeignKey<Provider>(provider => provider.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureCategories(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("categories");

            entity.HasKey(category => category.CategoryId);

            entity.Property(category => category.CategoryId)
                .HasColumnName("category_id");

            entity.Property(category => category.Name)
                .HasColumnName("name")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(category => category.Description)
                .HasColumnName("description")
                .HasColumnType("text");

            entity.Property(category => category.Status)
                .HasColumnName("status")
                .HasMaxLength(20)
                .HasDefaultValue("ACTIVE")
                .IsRequired();

            entity.Property(category => category.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("now()");

            entity.HasIndex(category => category.Name)
                .IsUnique();
        });
    }

    private static void ConfigureDestinations(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Destination>(entity =>
        {
            entity.ToTable("destinations");

            entity.HasKey(destination => destination.DestinationId);

            entity.Property(destination => destination.DestinationId)
                .HasColumnName("destination_id");

            entity.Property(destination => destination.Name)
                .HasColumnName("name")
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(destination => destination.Department)
                .HasColumnName("department")
                .HasMaxLength(100);

            entity.Property(destination => destination.Description)
                .HasColumnName("description")
                .HasColumnType("text");

            entity.Property(destination => destination.DestinationType)
                .HasColumnName("destination_type")
                .HasMaxLength(80);

            entity.Property(destination => destination.Status)
                .HasColumnName("status")
                .HasMaxLength(20)
                .HasDefaultValue("ACTIVE")
                .IsRequired();

            entity.Property(destination => destination.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("now()");

            entity.HasIndex(destination => new { destination.Name, destination.Department })
                .IsUnique();
        });
    }
}
