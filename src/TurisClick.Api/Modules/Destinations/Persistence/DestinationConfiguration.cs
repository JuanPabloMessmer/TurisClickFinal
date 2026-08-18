using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TurisClick.Api.Modules.Destinations.Entities;

namespace TurisClick.Api.Modules.Destinations.Persistence;

/// <summary>
/// Mapea 1:1 a la tabla `destinations` de docs/database-design.md.
/// Corrección respecto a la FASE 3 original: un único índice UNIQUE(name, parent_id, type) no basta,
/// porque Postgres trata cada NULL como distinto — dos países con el mismo nombre (parent_id = NULL en
/// ambos) no chocarían contra ese índice. Se separa en dos índices únicos parciales: uno para nodos raíz
/// (parent_id IS NULL) y otro para el resto.
/// </summary>
public class DestinationConfiguration : IEntityTypeConfiguration<Destination>
{
    public void Configure(EntityTypeBuilder<Destination> builder)
    {
        builder.ToTable("destinations", t => t.HasCheckConstraint(
            "ck_destinations_country_no_parent",
            "(type = 'COUNTRY' AND parent_id IS NULL) OR (type <> 'COUNTRY' AND parent_id IS NOT NULL)"));

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(d => d.Name)
            .HasColumnName("name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(d => d.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(d => d.ParentId)
            .HasColumnName("parent_id");

        builder.Property(d => d.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.HasOne(d => d.Parent)
            .WithMany(d => d.Children)
            .HasForeignKey(d => d.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => d.ParentId)
            .HasDatabaseName("ix_destinations_parent_id");

        builder.HasIndex(d => d.Type)
            .HasDatabaseName("ix_destinations_type");

        builder.HasIndex(d => new { d.Name, d.ParentId, d.Type })
            .IsUnique()
            .HasDatabaseName("uq_destinations_name_parent_type")
            .HasFilter("parent_id IS NOT NULL");

        builder.HasIndex(d => new { d.Name, d.Type })
            .IsUnique()
            .HasDatabaseName("uq_destinations_name_type_root")
            .HasFilter("parent_id IS NULL");
    }
}
