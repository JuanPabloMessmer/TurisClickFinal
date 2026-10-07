using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Auth.Dtos;

/// <summary>
/// Cambio de contraseña de la propia cuenta.
///
/// Pide la actual incluso cuando es la temporal: sin eso, un token robado alcanzaría para quedarse con la
/// cuenta. Y el mínimo de 10 caracteres existe porque la contraseña que reemplaza circuló por fuera del
/// sistema —se la dictaron o se la escribieron— y no tiene sentido cambiarla por otra igual de frágil.
/// </summary>
public class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, MinLength(10, ErrorMessage = "La contraseña nueva tiene que tener al menos 10 caracteres.")]
    [MaxLength(200)]
    public string NewPassword { get; set; } = string.Empty;
}
