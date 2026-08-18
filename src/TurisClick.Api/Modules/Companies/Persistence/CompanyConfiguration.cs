using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TurisClick.Api.Modules.Companies.Entities;

namespace TurisClick.Api.Modules.Companies.Persistence;

/// <summary>
/// Mapea 1:1 a la tabla `companies` de docs/database-design.md, y configura acá (no en
/// UserConfiguration) la relación Company 1—N User: la tabla `users` de Oleada 0 no necesita
/// cambiar su propia configuración solo porque un módulo nuevo agrega una relación hacia ella.
/// </summary>
public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("companies");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(c => c.Name)
            .HasColumnName("name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(c => c.Description)
            .HasColumnName("description");

        builder.Property(c => c.LegalDocument)
            .HasColumnName("legal_document")
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(c => c.LegalDocument)
            .IsUnique()
            .HasDatabaseName("uq_companies_legal_document");

        builder.Property(c => c.ContactEmail)
            .HasColumnName("contact_email")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(c => c.ContactPhone)
            .HasColumnName("contact_phone")
            .HasMaxLength(30);

        builder.Property(c => c.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(CompanyStatus.PENDING_APPROVAL);

        builder.Property(c => c.ApprovedByUserId)
            .HasColumnName("approved_by_user_id");

        builder.Property(c => c.ApprovedAt)
            .HasColumnName("approved_at");

        builder.Property(c => c.RejectionReason)
            .HasColumnName("rejection_reason");

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.HasIndex(c => c.Status)
            .HasDatabaseName("ix_companies_status");

        // ADMIN que aprobó/rechazó — no se cascadea: un usuario no se borra físicamente (ver domain-model.md).
        builder.HasOne(c => c.ApprovedByUser)
            .WithMany()
            .HasForeignKey(c => c.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Company 1—N User (usuarios PROVIDER de esa empresa).
        builder.HasMany(c => c.Users)
            .WithOne(u => u.Company)
            .HasForeignKey(u => u.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
