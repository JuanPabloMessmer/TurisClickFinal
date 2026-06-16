using Microsoft.EntityFrameworkCore;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureRoles(modelBuilder);
        ConfigureUsers(modelBuilder);
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

            entity.Property(user => user.RegisteredAt)
                .HasColumnName("registered_at")
                .HasDefaultValueSql("now()");

            entity.HasIndex(user => user.Email)
                .IsUnique();

            entity.HasOne(user => user.Role)
                .WithMany(role => role.Users)
                .HasForeignKey(user => user.RoleId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
