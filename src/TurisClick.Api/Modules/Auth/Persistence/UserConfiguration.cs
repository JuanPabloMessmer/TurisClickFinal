using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TurisClick.Api.Modules.Auth.Entities;

namespace TurisClick.Api.Modules.Auth.Persistence;

/// <summary>
/// Mapea 1:1 a la tabla `users` de docs/database-design.md.
/// Nota de implementación: role/status se guardan como varchar + CHECK (no ENUM nativo de Postgres) para
/// evitar la fragilidad del mapeo de enums nativos de Npgsql en tiempo de diseño/migraciones — mismo nivel
/// de integridad, y se puede migrar a ENUM nativo más adelante si hace falta. company_id es un uuid simple,
/// sin FK física todavía (ver User.cs).
/// </summary>
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", t => t.HasCheckConstraint(
            "ck_users_provider_has_company",
            "(role = 'PROVIDER' AND company_id IS NOT NULL) OR (role <> 'PROVIDER' AND company_id IS NULL)"));

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(u => u.FirstName)
            .HasColumnName("first_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(u => u.LastName)
            .HasColumnName("last_name")
            .HasMaxLength(100)
            .IsRequired();

        // Calculado en memoria a partir de FirstName/LastName — no tiene columna propia.
        builder.Ignore(u => u.FullName);

        builder.Property(u => u.Email)
            .HasColumnName("email")
            .HasMaxLength(255)
            .IsRequired();

        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasDatabaseName("uq_users_email");

        builder.Property(u => u.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(u => u.Role)
            .HasColumnName("role")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(u => u.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(UserStatus.ACTIVE);

        builder.Property(u => u.CompanyId)
            .HasColumnName("company_id");

        builder.HasIndex(u => u.CompanyId)
            .HasDatabaseName("ix_users_company_id");

        builder.Property(u => u.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.HasMany(u => u.RefreshTokens)
            .WithOne(rt => rt.User)
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
