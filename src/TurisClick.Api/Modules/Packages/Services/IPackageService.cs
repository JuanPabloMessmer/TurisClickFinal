using TurisClick.Api.Modules.Packages.Dtos;
using TurisClick.Api.Modules.Packages.Repositories;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Packages.Services;

public interface IPackageService
{
    /// <summary>UC-P-07. La Company se toma del claim del PROVIDER autenticado, nunca del request.</summary>
    Task<PackageResponse> CreateAsync(CreatePackageRequest request, CancellationToken ct);

    /// <summary>UC-P-08. Ownership validado contra el Package ya persistido (UC-SYS-03).</summary>
    Task<PackageResponse> UpdateAsync(Guid packageId, UpdatePackageRequest request, CancellationToken ct);

    /// <summary>UC-P-09. Exige al menos 1 PackageItem y al menos una disponibilidad futura con cupo.</summary>
    Task<PackageResponse> PublishAsync(Guid packageId, CancellationToken ct);

    /// <summary>UC-P-09.</summary>
    Task<PackageResponse> UnpublishAsync(Guid packageId, CancellationToken ct);

    /// <summary>Vista de detalle del PROVIDER dueño, en cualquier estado (soporte para editar/publicar).</summary>
    Task<PackageResponse> GetOwnedByIdAsync(Guid packageId, CancellationToken ct);

    /// <summary>"Mis paquetes" — todos los de la empresa del PROVIDER autenticado, cualquier estado.</summary>
    Task<PagedResult<PackageSummaryResponse>> ListOwnedAsync(int page, int pageSize, CancellationToken ct);

    /// <summary>UC-T-07 — público, solo PUBLISHED.</summary>
    Task<PackageResponse> GetPublishedByIdAsync(Guid id, CancellationToken ct);

    /// <summary>UC-T-06 — público, solo PUBLISHED.</summary>
    Task<PagedResult<PackageSummaryResponse>> SearchAsync(PackageSearchFilter filter, CancellationToken ct);
}
