using TurisClick.Api.Modules.Categories.Dtos;

namespace TurisClick.Api.Modules.Packages.Dtos;

/// <summary>Vista completa — usada por el detalle público (UC-T-07, solo PUBLISHED) y por el Provider dueño (cualquier estado).</summary>
public class PackageResponse
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public Guid DestinationId { get; set; }
    public string DestinationName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ConditionsText { get; set; }
    public int DurationDays { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<CategoryResponse> Categories { get; set; } = [];
    public List<PackageImageResponse> Images { get; set; } = [];

    /// <summary>Agrupados por día (DayNumber) y ordenados por SortOrder dentro de cada día.</summary>
    public List<PackageItemResponse> Items { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class PackageImageResponse
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsCover { get; set; }
}

public class PackageItemResponse
{
    public Guid Id { get; set; }
    public int DayNumber { get; set; }
    public int SortOrder { get; set; }
    public string Kind { get; set; } = string.Empty;
    public Guid? ExperienceId { get; set; }

    /// <summary>Si Kind = EXPERIENCE_REFERENCE y no se pisó Title, muestra el título real de la Experience.</summary>
    public string? Title { get; set; }
    public string? Description { get; set; }
}
