using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Modules.Experiences.Repositories;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Experiences.Services;

public interface IExperienceService
{
    /// <summary>UC-P-04. La Company se toma del claim del PROVIDER autenticado, nunca del request.</summary>
    Task<ExperienceResponse> CreateAsync(CreateExperienceRequest request, CancellationToken ct);

    /// <summary>UC-P-05. Ownership validado contra la Experience ya persistida (UC-SYS-03).</summary>
    Task<ExperienceResponse> UpdateAsync(Guid experienceId, UpdateExperienceRequest request, CancellationToken ct);

    /// <summary>UC-P-06. Exige al menos una disponibilidad futura con cupo (regla explícita, no solo el flag de estado).</summary>
    Task<ExperienceResponse> PublishAsync(Guid experienceId, CancellationToken ct);

    /// <summary>UC-P-06.</summary>
    Task<ExperienceResponse> UnpublishAsync(Guid experienceId, CancellationToken ct);

    /// <summary>Vista de detalle del PROVIDER dueño, en cualquier estado (soporte para editar/publicar).</summary>
    Task<ExperienceResponse> GetOwnedByIdAsync(Guid experienceId, CancellationToken ct);

    /// <summary>"Mis experiencias" — todas las de la empresa del PROVIDER autenticado, cualquier estado.</summary>
    Task<PagedResult<ExperienceSummaryResponse>> ListOwnedAsync(int page, int pageSize, CancellationToken ct);

    /// <summary>UC-T-05 — público, solo PUBLISHED.</summary>
    Task<ExperienceResponse> GetPublishedByIdAsync(Guid id, CancellationToken ct);

    /// <summary>UC-T-04 — público, solo PUBLISHED.</summary>
    Task<PagedResult<ExperienceSummaryResponse>> SearchAsync(ExperienceSearchFilter filter, CancellationToken ct);
}
