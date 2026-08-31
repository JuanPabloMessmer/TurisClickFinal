namespace TurisClick.Api.Modules.Packages.Repositories;

/// <summary>UC-T-06 — parámetros de búsqueda pública. Solo filtra sobre Package PUBLISHED (aplicado por el repositorio).</summary>
public record PackageSearchFilter(
    Guid? DestinationId,
    Guid? CategoryId,
    decimal? PriceMin,
    decimal? PriceMax,
    int? DurationDaysMin,
    int? DurationDaysMax,
    DateOnly? DepartureFrom,
    int Page,
    int PageSize);
