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
    IReadOnlyCollection<Guid> InterestCategoryIds);

public record RetrievalResult(IReadOnlyList<CandidateExperience> Experiences, IReadOnlyList<CandidatePackage> Packages);
