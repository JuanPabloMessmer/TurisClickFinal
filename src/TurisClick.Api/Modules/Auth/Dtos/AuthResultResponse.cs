namespace TurisClick.Api.Modules.Auth.Dtos;

/// <summary>Respuesta común de Register/Login/Refresh (UC-AUTH-01/02/03).</summary>
public class AuthResultResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public UserSummaryResponse User { get; set; } = new();
}

public class UserSummaryResponse
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    /// <summary>Calculado (FirstName + LastName), no es un campo propio del usuario — conveniencia para la UI.</summary>
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// true si la cuenta sigue con la contraseña temporal que le creó un administrador. Mientras lo esté, la
    /// API rechaza cualquier otra operación: el frontend lo usa para llevar directo a cambiarla, no para
    /// decidir si puede o no operar — eso lo decide el servidor.
    /// </summary>
    public bool MustChangePassword { get; set; }

    /// <summary>Solo tiene valor si Role = PROVIDER.</summary>
    public Guid? CompanyId { get; set; }
}
