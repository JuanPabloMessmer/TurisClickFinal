using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Admin.Dtos;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Reservations.Entities;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Admin.Services;

public class AdminContentService(
    ICurrentUserContext currentUser,
    ILogger<AdminContentService> logger,
    TurisClickDbContext db) : IAdminContentService
{
    public async Task<AdminContentResponse> SuspendExperienceAsync(Guid experienceId, CancellationToken ct)
    {
        var experience = await db.Experiences.FirstOrDefaultAsync(e => e.Id == experienceId, ct)
            ?? throw new NotFoundAppException("Experiencia no encontrada.");

        experience.Status = PublicationStatus.SUSPENDED;
        experience.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        LogSanction(ProductType.EXPERIENCE, experienceId, PublicationStatus.SUSPENDED);
        return ToResponse(experience);
    }

    public async Task<AdminContentResponse> RestoreExperienceAsync(Guid experienceId, CancellationToken ct)
    {
        var experience = await db.Experiences.FirstOrDefaultAsync(e => e.Id == experienceId, ct)
            ?? throw new NotFoundAppException("Experiencia no encontrada.");

        EnsureSuspended(experience.Status);

        // Vuelve a UNPUBLISHED, no a PUBLISHED: levantar la sanción devuelve el control al proveedor,
        // pero volver a publicar es una decisión suya (y vuelve a pasar por la validación de UC-P-06).
        experience.Status = PublicationStatus.UNPUBLISHED;
        experience.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        LogSanction(ProductType.EXPERIENCE, experienceId, PublicationStatus.UNPUBLISHED);
        return ToResponse(experience);
    }

    public async Task<AdminContentResponse> SuspendPackageAsync(Guid packageId, CancellationToken ct)
    {
        var package = await db.Packages.FirstOrDefaultAsync(p => p.Id == packageId, ct)
            ?? throw new NotFoundAppException("Paquete no encontrado.");

        package.Status = PublicationStatus.SUSPENDED;
        package.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        LogSanction(ProductType.PACKAGE, packageId, PublicationStatus.SUSPENDED);
        return ToResponse(package);
    }

    public async Task<AdminContentResponse> RestorePackageAsync(Guid packageId, CancellationToken ct)
    {
        var package = await db.Packages.FirstOrDefaultAsync(p => p.Id == packageId, ct)
            ?? throw new NotFoundAppException("Paquete no encontrado.");

        EnsureSuspended(package.Status);

        package.Status = PublicationStatus.UNPUBLISHED;
        package.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        LogSanction(ProductType.PACKAGE, packageId, PublicationStatus.UNPUBLISHED);
        return ToResponse(package);
    }

    private static void EnsureSuspended(PublicationStatus status)
    {
        if (status != PublicationStatus.SUSPENDED)
            throw new ConflictAppException("Este contenido no está suspendido.");
    }

    private void LogSanction(ProductType productType, Guid productId, PublicationStatus status) =>
        logger.LogInformation(
            "El admin {AdminId} dejó {ProductType} {ProductId} en estado {Status}.",
            currentUser.UserId, productType, productId, status);

    private static AdminContentResponse ToResponse(Experience experience) => new()
    {
        Id = experience.Id,
        ProductType = nameof(ProductType.EXPERIENCE),
        Title = experience.Title,
        Status = experience.Status.ToString(),
        CompanyId = experience.CompanyId
    };

    private static AdminContentResponse ToResponse(TurisClick.Api.Modules.Packages.Entities.Package package) => new()
    {
        Id = package.Id,
        ProductType = nameof(ProductType.PACKAGE),
        Title = package.Title,
        Status = package.Status.ToString(),
        CompanyId = package.CompanyId
    };
}
