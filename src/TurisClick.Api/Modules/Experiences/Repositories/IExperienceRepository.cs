using TurisClick.Api.Modules.Experiences.Entities;

namespace TurisClick.Api.Modules.Experiences.Repositories;

public interface IExperienceRepository
{
    /// <summary>AsNoTracking, sin relaciones — solo existencia + CompanyId (ej. para validar ownership antes de gestionar disponibilidad).</summary>
    Task<Experience?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>AsNoTracking, sin relaciones. Usado por Packages (Oleada 4) para validar en lote que cada PackageItem EXPERIENCE_REFERENCE pertenece a la misma Company que el Package.</summary>
    Task<List<Experience>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>AsNoTracking, con todas las relaciones cargadas — para armar una respuesta completa (pública o "mine").</summary>
    Task<Experience?> GetByIdForReadAsync(Guid id, CancellationToken ct);

    /// <summary>Tracked, con Categories/Images cargadas — para Update/Publish/Unpublish.</summary>
    Task<Experience?> GetByIdForUpdateAsync(Guid id, CancellationToken ct);

    Task<(List<Experience> Items, int TotalCount)> SearchAsync(ExperienceSearchFilter filter, CancellationToken ct);

    Task<(List<Experience> Items, int TotalCount)> ListByCompanyAsync(Guid companyId, int page, int pageSize, CancellationToken ct);

    Task<bool> HasFutureOpenAvailabilityAsync(Guid experienceId, CancellationToken ct);

    /// <summary>UC-A-04 DELETE — usado para dar un 409 de dominio claro en vez de dejar que la FK física falle en Postgres.</summary>
    Task<bool> ExistsForDestinationAsync(Guid destinationId, CancellationToken ct);

    Task AddAsync(Experience experience, CancellationToken ct);
}
