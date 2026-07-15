using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Categories.DTOs;

public class ChangeCategoryStatusRequest
{
    [Required]
    [RegularExpression("^(ACTIVE|INACTIVE)$")]
    public string Status { get; set; } = string.Empty;
}
