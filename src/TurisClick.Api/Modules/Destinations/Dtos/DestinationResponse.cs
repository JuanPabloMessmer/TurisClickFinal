namespace TurisClick.Api.Modules.Destinations.Dtos;

public class DestinationResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public string? ParentName { get; set; }

    /// <summary>Imagen representativa; null si todavía no tiene (el cliente muestra un fallback).</summary>
    public string? ImageUrl { get; set; }
}
