using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Packages.Entities;

namespace TurisClick.Api.Modules.Ai.Repositories;

/// <summary>
/// UC-AI-02 — retrieval DB-first: queries EF normales, nunca SQL generado por el LLM (docs de la
/// sesión, sección 9). Filtra por destino/precio/fecha/PUBLISHED en la base — el LLM jamás ve el
/// catálogo completo.
/// </summary>
public record AiCatalogFilter(Guid? DestinationId, decimal? PriceMax, DateOnly? DateFrom, DateOnly? DateTo, int SqlLimit);

public interface IAiCatalogRepository
{
    /// <summary>Con Destination/Categories/Company/Images/Availabilities cargados — lo que necesita el scoring y la vista de candidato.</summary>
    Task<List<Experience>> SearchCandidateExperiencesAsync(AiCatalogFilter filter, CancellationToken ct);

    Task<List<Package>> SearchCandidatePackagesAsync(AiCatalogFilter filter, CancellationToken ct);

    /// <summary>
    /// Estado ACTUAL (no el snapshot) de los productos ya referenciados por un itinerario — sin filtrar
    /// por PUBLISHED ni por disponibilidad, justamente porque la revalidación (UC-T-17, sección 9 de la
    /// sesión) necesita poder detectar que un producto se despublicó o se quedó sin cupos.
    /// </summary>
    Task<List<Experience>> GetExperiencesByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    Task<List<Package>> GetPackagesByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
