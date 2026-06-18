using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Users.DTOs;

public class ChangeUserRoleRequest
{
    [Required]
    [RegularExpression("^(ADMIN|TOURIST|PROVIDER)$")]
    public string Role { get; set; } = string.Empty;
}
