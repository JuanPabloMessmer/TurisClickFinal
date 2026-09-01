using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TurisClick.Api.Modules.Ai.Entities;

namespace TurisClick.Api.Modules.Ai.Persistence;

/// <summary>Mapea 1:1 a la tabla `ai_messages` de docs/database-design.md.</summary>
public class AiMessageConfiguration : IEntityTypeConfiguration<AiMessage>
{
    public void Configure(EntityTypeBuilder<AiMessage> builder)
    {
        builder.ToTable("ai_messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(m => m.AiConversationId).HasColumnName("ai_conversation_id").IsRequired();

        builder.Property(m => m.Sender)
            .HasColumnName("sender")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(m => m.Content).HasColumnName("content").IsRequired();
        builder.Property(m => m.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        builder.HasIndex(m => new { m.AiConversationId, m.CreatedAt }).HasDatabaseName("ix_ai_messages_conversation_created");
    }
}
