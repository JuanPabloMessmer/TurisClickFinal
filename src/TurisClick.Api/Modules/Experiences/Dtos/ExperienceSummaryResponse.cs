namespace TurisClick.Api.Modules.Experiences.Dtos;

/// <summary>Fila de resultados de búsqueda (UC-T-04) y de "mis experiencias" (Provider) — más liviana que ExperienceResponse.</summary>
public class ExperienceSummaryResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string DestinationName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? DurationLabel { get; set; }
    public string? CoverImageUrl { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
