using TurisClick.Api.Modules.Experiences.Entities;

namespace TurisClick.Api.Modules.Experiences.Repositories;

public interface IExperienceAvailabilityRepository
{
    Task<ExperienceAvailability?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>AsNoTracking, con Experience cargada — usado por UC-T-08 para validar en una sola consulta.</summary>
    Task<ExperienceAvailability?> GetByIdWithExperienceAsync(Guid id, CancellationToken ct);
    Task<bool> ExistsAsync(Guid experienceId, DateOnly date, TimeOnly? startTime, CancellationToken ct);

    /// <summary>Vista de gestión del PROVIDER dueño — todos los slots, cualquier fecha/estado.</summary>
    Task<List<ExperienceAvailability>> ListAllAsync(Guid experienceId, CancellationToken ct);

    /// <summary>Vista pública para reservar (UC-T-08) — solo OPEN, futuros y con cupo.</summary>
    Task<List<ExperienceAvailability>> ListBookableAsync(Guid experienceId, CancellationToken ct);

    Task AddAsync(ExperienceAvailability availability, CancellationToken ct);
}
