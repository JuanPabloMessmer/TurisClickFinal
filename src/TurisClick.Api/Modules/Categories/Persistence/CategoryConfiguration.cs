using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TurisClick.Api.Modules.Categories.Entities;

namespace TurisClick.Api.Modules.Categories.Persistence;

/// <summary>Mapea 1:1 a la tabla `categories` de docs/database-design.md.</summary>
public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(c => c.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(c => c.Name)
            .IsUnique()
            .HasDatabaseName("uq_categories_name");

        // Además existe uq_categories_name_key: índice único por expresión sobre el nombre normalizado (sin
        // mayúsculas, tildes ni espacios repetidos — NameKeySql.NameKey). EF Core no modela índices por
        // expresión, así que lo crea la migración 0016_Fase1_Validaciones con SQL. No borrar.

        builder.Property(c => c.Description)
            .HasColumnName("description");
    }
}
