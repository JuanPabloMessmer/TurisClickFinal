using System.ComponentModel.DataAnnotations;
using TurisClick.Api.Modules.Companies.Dtos;

namespace TurisClick.Api.Modules.Admin.Dtos;

/// <summary>
/// Lo que el administrador carga para dar de alta a un operador.
///
/// No hay contraseña acá a propósito: la genera el servidor. Dejar que un administrador elija la contraseña
/// de otra persona es la forma más rápida de terminar con cinco cuentas que comparten la misma.
/// </summary>
public class CreateProviderAccountRequest
{
    [Required, MinLength(2), MaxLength(150)]
    public string CompanyName { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? CompanyDescription { get; set; }

    [Required, MinLength(3), MaxLength(50)]
    public string LegalDocument { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(150)]
    public string ContactEmail { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? ContactPhone { get; set; }

    [Required, MinLength(2), MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MinLength(2), MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>Email con el que el operador va a entrar. Puede ser distinto del de contacto de la empresa.</summary>
    [Required, EmailAddress, MaxLength(150)]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Si la empresa queda aprobada de entrada. Un administrador que da de alta a un operador que ya conoce
    /// no tiene por qué aprobarse a sí mismo en un segundo paso.
    /// </summary>
    public bool Approve { get; set; } = true;
}

/// <summary>
/// Resultado del alta. La contraseña temporal viaja **una sola vez**, en esta respuesta, y no se guarda en
/// ningún lado en claro: de ella sólo queda el hash, igual que con cualquier otra contraseña.
///
/// Se devuelve en vez de enviarse por correo porque no hay infraestructura de email en el sistema, y montar
/// un envío falso sería peor que decir la verdad: el administrador se la pasa al operador por el canal que
/// ya usan.
/// </summary>
public class ProviderAccountCreatedResponse
{
    public CompanySummaryResponse Company { get; set; } = new();

    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;

    /// <summary>Contraseña temporal. No vuelve a estar disponible: si se pierde, se regenera.</summary>
    public string TemporaryPassword { get; set; } = string.Empty;

    /// <summary>Siempre true al crear: la cuenta no puede operar hasta cambiarla.</summary>
    public bool MustChangePassword { get; set; } = true;
}

/// <summary>Regenerar la credencial temporal de un operador que perdió la suya.</summary>
public class ResetProviderPasswordResponse
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string TemporaryPassword { get; set; } = string.Empty;
}

/// <summary>Una cuenta de operador, como la ve el administrador en el detalle de la empresa.</summary>
public class CompanyUserResponse
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    /// <summary>true si todavía no cambió la contraseña temporal con la que se la creó.</summary>
    public bool MustChangePassword { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
