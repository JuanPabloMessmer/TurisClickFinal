using System.ComponentModel.DataAnnotations;
using TurisClick.Api.Modules.Companies.Dtos;
using TurisClick.Api.Shared.Validation;

namespace TurisClick.Api.Modules.Admin.Dtos;

/// <summary>
/// Lo que el administrador carga para dar de alta a un operador.
///
/// No hay contraseña acá a propósito: la genera el servidor. Dejar que un administrador elija la contraseña
/// de otra persona es la forma más rápida de terminar con cinco cuentas que comparten la misma.
/// </summary>
///
/// Reglas R-01 a R-06 de docs/auditoria-validaciones.md: cada atributo valida el valor normalizado y el
/// servicio guarda ese mismo valor.
public class CreateProviderAccountRequest
{
    [Required(AllowEmptyStrings = true)] // obligatorio en el contrato OpenAPI; el mensaje lo da la regla propia
    [Rule.OrganizationName]
    public string CompanyName { get; set; } = string.Empty;

    [Rule.PlainText("La descripción", ValidationLimits.CompanyDescriptionMax, MultiLine = true)]
    public string? CompanyDescription { get; set; }

    /// <summary>NIT boliviano: 5 a 12 dígitos; se aceptan espacios, puntos y guiones, que se quitan al guardar.</summary>
    [Required(AllowEmptyStrings = true)] // obligatorio en el contrato OpenAPI; el mensaje lo da la regla propia
    [Rule.BolivianNit]
    public string LegalDocument { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = true)] // obligatorio en el contrato OpenAPI; el mensaje lo da la regla propia
    [Rule.StrictEmail(Label = "El correo de contacto")]
    public string ContactEmail { get; set; } = string.Empty;

    [Rule.ContactPhone]
    public string? ContactPhone { get; set; }

    [Required(AllowEmptyStrings = true)] // obligatorio en el contrato OpenAPI; el mensaje lo da la regla propia
    [Rule.PersonName("El nombre")]
    public string FirstName { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = true)] // obligatorio en el contrato OpenAPI; el mensaje lo da la regla propia
    [Rule.PersonName("El apellido")]
    public string LastName { get; set; } = string.Empty;

    /// <summary>Email con el que el operador va a entrar. Puede ser distinto del de contacto de la empresa.</summary>
    [Required(AllowEmptyStrings = true)] // obligatorio en el contrato OpenAPI; el mensaje lo da la regla propia
    [Rule.StrictEmail(Label = "El correo de acceso")]
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

/// <summary>
/// Lo que un administrador carga para dar de alta a otro administrador. Igual que en el alta de operadores,
/// no hay contraseña: la genera el servidor.
/// </summary>
public class CreateAdminAccountRequest
{
    [Required(AllowEmptyStrings = true)] // obligatorio en el contrato OpenAPI; el mensaje lo da la regla propia
    [Rule.PersonName("El nombre")]
    public string FirstName { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = true)] // obligatorio en el contrato OpenAPI; el mensaje lo da la regla propia
    [Rule.PersonName("El apellido")]
    public string LastName { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = true)] // obligatorio en el contrato OpenAPI; el mensaje lo da la regla propia
    [Rule.StrictEmail(Label = "El correo de acceso")]
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// Resultado del alta de un administrador. La contraseña temporal viaja **una sola vez**, en esta respuesta;
/// de ella sólo queda el hash.
/// </summary>
public class AdminAccountCreatedResponse
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;

    /// <summary>Contraseña temporal. No vuelve a estar disponible.</summary>
    public string TemporaryPassword { get; set; } = string.Empty;

    /// <summary>Siempre true al crear: la cuenta no puede operar hasta cambiarla.</summary>
    public bool MustChangePassword { get; set; } = true;
}

/// <summary>
/// Regenerar la credencial temporal de otro administrador. Como al darlo de alta, la contraseña viaja una sola
/// vez y la cuenta no puede operar hasta cambiarla.
/// </summary>
public class ResetAdminPasswordResponse
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string TemporaryPassword { get; set; } = string.Empty;

    /// <summary>Siempre true: la cuenta vuelve a tener que elegir su contraseña.</summary>
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
