using TurisClick.Api.Modules.Admin.Dtos;

namespace TurisClick.Api.Modules.Admin.Services;

/// <summary>
/// UC-A-07 — sanción administrativa sobre contenido. `SUSPENDED` no es un estado más de publicación:
/// solo un ADMIN puede ponerlo y solo un ADMIN puede levantarlo (decisión de dominio Oleada 8). El
/// proveedor no puede republicar por su cuenta lo que le suspendieron.
/// </summary>
public interface IAdminContentService
{
    Task<AdminContentResponse> SuspendExperienceAsync(Guid experienceId, CancellationToken ct);

    /// <summary>Levanta la sanción devolviendo el contenido a UNPUBLISHED: vuelve a manos del proveedor, pero no se republica solo.</summary>
    Task<AdminContentResponse> RestoreExperienceAsync(Guid experienceId, CancellationToken ct);

    Task<AdminContentResponse> SuspendPackageAsync(Guid packageId, CancellationToken ct);

    Task<AdminContentResponse> RestorePackageAsync(Guid packageId, CancellationToken ct);
}
