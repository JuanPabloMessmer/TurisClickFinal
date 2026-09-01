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
}
