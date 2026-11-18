using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Auth.Dtos;

/// <summary>UC-AUTH-02 — Iniciar sesión.</summary>
public class LoginRequest
{
    [Required(ErrorMessage = "Ingresá tu correo.")]
    [EmailAddress(ErrorMessage = "Ingresá un correo válido.")]
    [MaxLength(254, ErrorMessage = "El correo puede tener hasta 254 caracteres.")]
    public string Email { get; set; } = string.Empty;

    // Sin mínimo a propósito: las contraseñas que ya existen (algunas de 8 o 9 caracteres) tienen que seguir
    // sirviendo para entrar. El mínimo de 10 rige sólo al crearlas o cambiarlas (docs/auditoria-validaciones.md, D-7).
    [Required(ErrorMessage = "Ingresá tu contraseña.")]
    public string Password { get; set; } = string.Empty;
}
