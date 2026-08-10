using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Auth.Dtos;

/// <summary>UC-AUTH-04 — Cerrar sesión (revoca el refresh token indicado).</summary>
public class LogoutRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
