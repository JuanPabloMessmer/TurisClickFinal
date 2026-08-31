using TurisClick.Api.Modules.Packages.Dtos;

namespace TurisClick.Api.Modules.Packages.Services;

public interface IPackageAvailabilityService
{
    /// <summary>UC-P-11. Ownership validado contra el Package dueño.</summary>
    Task<PackageAvailabilityResponse> CreateAsync(Guid packageId, CreatePackageAvailabilityRequest request, CancellationToken ct);

    /// <summary>Vista de gestión del PROVIDER dueño — todas las salidas, cualquier fecha/estado.</summary>
    Task<List<PackageAvailabilityResponse>> ListOwnedAsync(Guid packageId, CancellationToken ct);

    /// <summary>Vista pública — solo si el Package está PUBLISHED; solo salidas reservables (UC-T-09).</summary>
    Task<List<PackageAvailabilityResponse>> ListPublicAsync(Guid packageId, CancellationToken ct);
}
