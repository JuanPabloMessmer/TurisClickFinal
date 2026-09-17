using TurisClick.Api.Modules.Packages.Dtos;

namespace TurisClick.Api.Modules.Packages.Services;

public interface IPackageAvailabilityService
{
    /// <summary>UC-P-11. Ownership validado contra el Package dueño.</summary>
    Task<PackageAvailabilityResponse> CreateAsync(Guid packageId, CreatePackageAvailabilityRequest request, CancellationToken ct);

    /// <summary>Alta masiva de salidas por calendario; omite lo existente; transaccional.</summary>
    Task<BulkPackageAvailabilityResponse> BulkCreateAsync(Guid packageId, BulkCreatePackageAvailabilityRequest request, CancellationToken ct);

    /// <summary>Ajusta cupo (nunca por debajo de lo reservado) y/o abre/cierra una salida.</summary>
    Task<PackageAvailabilityResponse> UpdateAsync(Guid packageId, Guid availabilityId, Experiences.Dtos.UpdateAvailabilityRequest request, CancellationToken ct);

    /// <summary>Vista de gestión del PROVIDER dueño — todas las salidas, cualquier fecha/estado.</summary>
    Task<List<PackageAvailabilityResponse>> ListOwnedAsync(Guid packageId, CancellationToken ct);

    /// <summary>Vista pública — solo si el Package está PUBLISHED; solo salidas reservables (UC-T-09).</summary>
    Task<List<PackageAvailabilityResponse>> ListPublicAsync(Guid packageId, CancellationToken ct);
}
