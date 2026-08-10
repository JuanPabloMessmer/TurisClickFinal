using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Auth.Dtos;

/// <summary>UC-AUTH-02 — Iniciar sesión.</summary>
public class LoginRequest
{
    [Required, EmailAddress, MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
