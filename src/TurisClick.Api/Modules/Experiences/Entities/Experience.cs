using TurisClick.Api.Modules.Categories.Entities;
using TurisClick.Api.Modules.Companies.Entities;
using TurisClick.Api.Modules.Destinations.Entities;

namespace TurisClick.Api.Modules.Experiences.Entities;

/// <summary>docs/domain-model.md §4. Soporta UC-P-04/05/06/10, UC-T-04/05.</summary>
public class Experience
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public Guid DestinationId { get; set; }
    public Destination? Destination { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? IncludesText { get; set; }
    public string? ExcludesText { get; set; }
    public int? DurationMinutes { get; set; }
    public string? DurationLabel { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public PublicationStatus Status { get; set; } = PublicationStatus.DRAFT;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<ExperienceImage> Images { get; set; } = new List<ExperienceImage>();
    public ICollection<ExperienceAvailability> Availabilities { get; set; } = new List<ExperienceAvailability>();
    public ICollection<Category> Categories { get; set; } = new List<Category>();
}
