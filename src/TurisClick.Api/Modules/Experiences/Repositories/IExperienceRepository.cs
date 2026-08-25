using TurisClick.Api.Modules.Experiences.Entities;

namespace TurisClick.Api.Modules.Experiences.Repositories;

public interface IExperienceRepository
{
    /// <summary>AsNoTracking, sin relaciones — solo existencia + CompanyId (ej. para validar ownership antes de gestionar disponibilidad).</summary>
    Task<Experience?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>AsNoTracking, con todas las relaciones cargadas — para armar una respuesta completa (pública o "mine").</summary>
    Task<Experience?> GetByIdForReadAsync(Guid id, CancellationToken ct);

    /// <summary>Tracked, con Categories/Images cargadas — para Update/Publish/Unpublish.</summary>
    Task<Experience?> GetByIdForUpdateAsync(Guid id, CancellationToken ct);

    Task<(List<Experience> Items, int TotalCount)> SearchAsync(ExperienceSearchFilter filter, CancellationToken ct);

    Task<(List<Experience> Items, int TotalCount)> ListByCompanyAsync(Guid companyId, int page, int pageSize, CancellationToken ct);

    Task<bool> HasFutureOpenAvailabilityAsync(Guid experienceId, CancellationToken ct);

    Task AddAsync(Experience experience, CancellationToken ct);
}
