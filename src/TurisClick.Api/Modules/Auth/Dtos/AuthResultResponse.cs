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

    /// <summary>Solo tiene valor si Role = PROVIDER.</summary>
    public Guid? CompanyId { get; set; }
}
