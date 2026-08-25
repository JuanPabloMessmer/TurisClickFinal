namespace TurisClick.Api.Modules.Experiences.Repositories;

/// <summary>UC-T-04 — parámetros de búsqueda pública. Solo filtra sobre Experience PUBLISHED (aplicado por el repositorio).</summary>
public record ExperienceSearchFilter(
    Guid? DestinationId,
    Guid? CategoryId,
    decimal? PriceMin,
    decimal? PriceMax,
    DateOnly? AvailableFrom,
    int Page,
    int PageSize);
