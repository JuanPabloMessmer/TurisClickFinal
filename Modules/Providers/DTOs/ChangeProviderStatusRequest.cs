using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Providers.DTOs;

public class ChangeProviderStatusRequest
{
    [Required]
    [RegularExpression("^(ACTIVE|INACTIVE|BLOCKED)$")]
    public string Status { get; set; } = string.Empty;
}
