using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Users.DTOs;

public class ChangeUserStatusRequest
{
    [Required]
    [RegularExpression("^(ACTIVE|INACTIVE|BLOCKED)$")]
    public string Status { get; set; } = string.Empty;
}
