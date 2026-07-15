using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Destinations.DTOs;

public class CreateDestinationRequest
{
    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Department { get; set; }

    public string? Description { get; set; }

    [MaxLength(80)]
    public string? DestinationType { get; set; }
}
