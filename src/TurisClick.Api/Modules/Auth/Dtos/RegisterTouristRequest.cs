using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Auth.Dtos;

/// <summary>UC-AUTH-01 — Registrar cuenta de Turista.</summary>
public class RegisterTouristRequest
{
    [Required, MinLength(2), MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(100)]
    public string Password { get; set; } = string.Empty;
}
