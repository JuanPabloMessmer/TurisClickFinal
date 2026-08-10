using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TurisClick.Api.Modules.Auth.Entities;

namespace TurisClick.Api.Modules.Auth.Persistence;

/// <summary>Mapea 1:1 a la tabla `refresh_tokens` de docs/database-design.md.</summary>
public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(rt => rt.Id);
        builder.Property(rt => rt.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(rt => rt.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.HasIndex(rt => rt.UserId)
            .HasDatabaseName("ix_refresh_tokens_user_id");

        builder.Property(rt => rt.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(255)
            .IsRequired();

        builder.HasIndex(rt => rt.TokenHash)
            .IsUnique()
            .HasDatabaseName("uq_refresh_tokens_token_hash");

        builder.Property(rt => rt.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired();

        builder.Property(rt => rt.RevokedAt)
            .HasColumnName("revoked_at");

        builder.Property(rt => rt.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Ignore(rt => rt.IsActive);
    }
}
