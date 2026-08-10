using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Auth.Dtos;

/// <summary>UC-AUTH-03 — Refrescar token de sesión.</summary>
public class RefreshTokenRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
