namespace TurisClick.Api.Modules.Packages.Dtos;

/// <summary>Fila de resultados de búsqueda (UC-T-06) y de "mis paquetes" (Provider) — más liviana que PackageResponse.</summary>
public class PackageSummaryResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string DestinationName { get; set; } = string.Empty;
    public int DurationDays { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
