namespace TurisClick.Api.Modules.Ai.Services;

/// <summary>
/// UC-AI-02/03 — retrieval estructurado (DB-first) + scoring determinístico de "¿este Package encaja?".
/// Nombre reservado en docs/backend-architecture.md §14. Nunca genera SQL a partir de texto del LLM: el
/// LLM solo recibe la salida ya filtrada y acotada de este servicio.
/// </summary>
public interface IRetrievalService
{
    Task<RetrievalResult> RetrieveAsync(RetrievalQuery query, CancellationToken ct);
}

public record RetrievalQuery(
    Guid? PreferredDestinationId,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int? DurationDays,
    decimal? BudgetPerPerson,
    string? BudgetCurrency,
    IReadOnlyCollection<Guid> InterestCategoryIds,
    /// <summary>
    /// UC-AI-05 — productos que NO deben volver a ofrecerse al iterar: los que el turista acaba de pedir
    /// sacar ("no quiero rafting") y los que ya quedaron preservados en el itinerario (para no duplicarlos).
    /// </summary>
    IReadOnlyCollection<Guid> ExcludedProductIds);

public record RetrievalResult(IReadOnlyList<CandidateExperience> Experiences, IReadOnlyList<CandidatePackage> Packages);
