using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Destinations.DTOs;

public class ChangeDestinationStatusRequest
{
    [Required]
    [RegularExpression("^(ACTIVE|INACTIVE)$")]
    public string Status { get; set; } = string.Empty;
}
