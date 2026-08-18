using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Companies.Dtos;

/// <summary>UC-P-01 — Registrar empresa y solicitar cuenta de Provider.</summary>
public class RegisterProviderRequest
{
    [Required, MinLength(2), MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MinLength(2), MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(100)]
    public string Password { get; set; } = string.Empty;

    [Required, MinLength(2), MaxLength(150)]
    public string CompanyName { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? CompanyDescription { get; set; }

    [Required, MaxLength(50)]
    public string LegalDocument { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(255)]
    public string ContactEmail { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? ContactPhone { get; set; }
}
