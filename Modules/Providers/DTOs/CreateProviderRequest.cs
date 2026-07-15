using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Providers.DTOs;

public class CreateProviderRequest
{
    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? LastName { get; set; }

    [Required]
    [EmailAddress]
    [MaxLength(150)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    [MaxLength(100)]
    public string Password { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? Phone { get; set; }

    [Required]
    [MaxLength(150)]
    public string CompanyName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Nit { get; set; }

    public string? Description { get; set; }

    [MaxLength(30)]
    public string? ContactPhone { get; set; }

    [EmailAddress]
    [MaxLength(150)]
    public string? ContactEmail { get; set; }

    [MaxLength(255)]
    public string? Address { get; set; }

    [MaxLength(500)]
    public string? LogoUrl { get; set; }
}
