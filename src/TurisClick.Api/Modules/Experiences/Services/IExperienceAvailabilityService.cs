using TurisClick.Api.Modules.Experiences.Dtos;

namespace TurisClick.Api.Modules.Experiences.Services;

public interface IExperienceAvailabilityService
{
    /// <summary>UC-P-10. Ownership validado contra la Experience dueña.</summary>
    Task<ExperienceAvailabilityResponse> CreateAsync(Guid experienceId, CreateExperienceAvailabilityRequest request, CancellationToken ct);

    /// <summary>Alta masiva por calendario: expande rango × patrón × horarios; omite lo existente; transaccional.</summary>
    Task<BulkExperienceAvailabilityResponse> BulkCreateAsync(Guid experienceId, BulkCreateExperienceAvailabilityRequest request, CancellationToken ct);

    /// <summary>Ajusta cupo (nunca por debajo de lo reservado) y/o abre/cierra una fecha. Cerrar solo frena reservas nuevas.</summary>
    Task<ExperienceAvailabilityResponse> UpdateAsync(Guid experienceId, Guid availabilityId, UpdateAvailabilityRequest request, CancellationToken ct);

    /// <summary>Vista de gestión del PROVIDER dueño — todos los slots, cualquier fecha/estado.</summary>
    Task<List<ExperienceAvailabilityResponse>> ListOwnedAsync(Guid experienceId, CancellationToken ct);

    /// <summary>Vista pública — solo si la Experience está PUBLISHED; solo slots reservables (UC-T-08).</summary>
    Task<List<ExperienceAvailabilityResponse>> ListPublicAsync(Guid experienceId, CancellationToken ct);
}
