namespace TurisClick.Api.Modules.Destinations.DTOs;

public class DestinationResponse
{
    public int DestinationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Description { get; set; }
    public string? DestinationType { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
