using TurisClick.Api.Modules.Categories.Dtos;

namespace TurisClick.Api.Modules.Experiences.Dtos;

/// <summary>Vista completa — usada por el detalle público (UC-T-05, solo PUBLISHED) y por el Provider dueño (cualquier estado).</summary>
public class ExperienceResponse
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public Guid DestinationId { get; set; }
    public string DestinationName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? IncludesText { get; set; }
    public string? ExcludesText { get; set; }
    public int? DurationMinutes { get; set; }
    public string? DurationLabel { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<CategoryResponse> Categories { get; set; } = [];
    public List<ExperienceImageResponse> Images { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class ExperienceImageResponse
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsCover { get; set; }
}
