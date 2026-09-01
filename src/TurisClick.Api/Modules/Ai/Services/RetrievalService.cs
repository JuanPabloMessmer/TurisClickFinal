using Microsoft.Extensions.Options;
using TurisClick.Api.Modules.Ai.Repositories;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Packages.Entities;

namespace TurisClick.Api.Modules.Ai.Services;

public class RetrievalService(IAiCatalogRepository catalogRepository, IOptions<AiOptions> options) : IRetrievalService
{
    /// <summary>Techo de filas que trae la query SQL antes de rankear en memoria — nunca "todo el catálogo" (sección 9).</summary>
    private const int SqlCandidateCap = 50;

    public async Task<RetrievalResult> RetrieveAsync(RetrievalQuery query, CancellationToken ct)
    {
        // No se aplica PriceMax en la query SQL: el presupuesto está en una moneda concreta y no
        // sabemos de antemano la moneda de cada candidato — comparar precio/presupuesto solo tiene
        // sentido cuando coinciden (sección 12, "no inventar conversión FX"). El filtro de precio se
        // aplica más abajo, en el scoring, solo entre monedas iguales.
        var filter = new AiCatalogFilter(query.PreferredDestinationId, PriceMax: null, query.StartDate, query.EndDate, SqlCandidateCap);

        var experiences = await catalogRepository.SearchCandidateExperiencesAsync(filter, ct);
        var packages = await catalogRepository.SearchCandidatePackagesAsync(filter, ct);

        var maxPerType = options.Value.MaxCandidatesPerType;

        var rankedExperiences = experiences
            .Select(e => (Entity: e, Score: ScoreExperience(e, query)))
            .OrderByDescending(x => x.Score)
            .Take(maxPerType)
            .Select(x => ToCandidate(x.Entity))
            .ToList();

        var rankedPackages = packages
            .Select(p =>
            {
                var score = ScorePackage(p, query, out var isStrongFit);
                return (Entity: p, Score: score, IsStrongFit: isStrongFit);
            })
            .OrderByDescending(x => x.Score)
            .Take(maxPerType)
            .Select(x => ToCandidate(x.Entity, x.IsStrongFit))
            .ToList();

        return new RetrievalResult(rankedExperiences, rankedPackages);
    }

    private static int ScoreExperience(Experience experience, RetrievalQuery query)
    {
        var score = 0;
        score += SharedCategoryCount(experience.Categories.Select(c => c.Id), query.InterestCategoryIds);
        if (FitsBudget(experience.Price, experience.Currency, query.BudgetPerPerson, query.BudgetCurrency))
            score += 2;
        return score;
    }

    /// <summary>
    /// UC-AI-03 — determinístico: destino ya viene filtrado por SQL; acá se puntúa duración/categorías/
    /// presupuesto. IsStrongFit = true si el Package cubre razonablemente bien el pedido, señal que
    /// después el LLM puede usar para recomendarlo como base en vez de descomponer todo en Experiences.
    /// </summary>
    private static int ScorePackage(Package package, RetrievalQuery query, out bool isStrongFit)
    {
        var score = 0;
        var durationFits = false;

        if (query.DurationDays is { } tripDays && tripDays > 0)
        {
            durationFits = package.DurationDays <= tripDays && tripDays - package.DurationDays <= 2;
            if (durationFits) score += 3;
        }
        else
        {
            // Sin duración pedida todavía, no penalizamos ni premiamos por duración.
            durationFits = true;
        }

        var sharedCategories = SharedCategoryCount(package.Categories.Select(c => c.Id), query.InterestCategoryIds);
        score += sharedCategories;

        var budgetFits = FitsBudget(package.Price, package.Currency, query.BudgetPerPerson, query.BudgetCurrency);
        if (budgetFits) score += 2;

        // Fit fuerte: duración razonable + al menos una categoría de interés compartida (cuando el
        // turista especificó intereses) + dentro de presupuesto CUANDO se puede comparar (mismo
        // moneda) — un presupuesto en otra moneda no descarta el Package (sección 12: no se inventa
        // conversión FX), simplemente no aporta ni resta al fit.
        var categoriesOk = query.InterestCategoryIds.Count == 0 || sharedCategories > 0;
        var canCompareBudget = query.BudgetPerPerson is not null
            && string.Equals(package.Currency, query.BudgetCurrency, StringComparison.OrdinalIgnoreCase);
        var budgetOk = !canCompareBudget || budgetFits;
        isStrongFit = durationFits && categoriesOk && budgetOk;

        return score;
    }

    private static int SharedCategoryCount(IEnumerable<Guid> candidateCategoryIds, IReadOnlyCollection<Guid> interestCategoryIds)
    {
        if (interestCategoryIds.Count == 0) return 0;
        var interestSet = interestCategoryIds.ToHashSet();
        return candidateCategoryIds.Count(interestSet.Contains);
    }

    private static bool FitsBudget(decimal price, string currency, decimal? budget, string? budgetCurrency)
    {
        if (budget is null) return false;
        if (!string.Equals(currency, budgetCurrency, StringComparison.OrdinalIgnoreCase)) return false;
        return price <= budget;
    }

    private static CandidateExperience ToCandidate(Experience experience) => new(
        experience.Id,
        experience.Title,
        experience.Destination?.Name ?? string.Empty,
        experience.Price,
        experience.Currency,
        experience.Categories.Select(c => c.Name).ToList(),
        experience.DurationMinutes,
        experience.Availabilities
            .Select(a => new CandidateAvailability(a.Id, a.Date, a.AvailableSlots))
            .ToList());

    private static CandidatePackage ToCandidate(Package package, bool isStrongFit) => new(
        package.Id,
        package.Title,
        package.Destination?.Name ?? string.Empty,
        package.Price,
        package.Currency,
        package.DurationDays,
        package.Categories.Select(c => c.Name).ToList(),
        package.Availabilities
            .Select(a => new CandidateAvailability(a.Id, a.DepartureDate, a.AvailableSlots))
            .ToList(),
        isStrongFit);
}
