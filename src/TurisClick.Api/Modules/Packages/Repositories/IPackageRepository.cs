using TurisClick.Api.Modules.Packages.Entities;

namespace TurisClick.Api.Modules.Packages.Repositories;

public interface IPackageRepository
{
    /// <summary>AsNoTracking, sin relaciones — solo existencia + CompanyId (ej. para validar ownership antes de gestionar disponibilidad).</summary>
    Task<Package?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>AsNoTracking, con todas las relaciones cargadas — para armar una respuesta completa (pública o "mine").</summary>
    Task<Package?> GetByIdForReadAsync(Guid id, CancellationToken ct);

    /// <summary>Tracked, con Items/Images/Categories cargadas — para Update/Publish/Unpublish.</summary>
    Task<Package?> GetByIdForUpdateAsync(Guid id, CancellationToken ct);

    Task<(List<Package> Items, int TotalCount)> SearchAsync(PackageSearchFilter filter, CancellationToken ct);

    Task<(List<Package> Items, int TotalCount)> ListByCompanyAsync(Guid companyId, int page, int pageSize, CancellationToken ct);

    Task<bool> HasFutureOpenAvailabilityAsync(Guid packageId, CancellationToken ct);

    /// <summary>UC-A-04 DELETE — usado para dar un 409 de dominio claro en vez de dejar que la FK física falle en Postgres.</summary>
    Task<bool> ExistsForDestinationAsync(Guid destinationId, CancellationToken ct);

    Task AddAsync(Package package, CancellationToken ct);
}
