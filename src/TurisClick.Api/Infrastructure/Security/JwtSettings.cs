namespace TurisClick.Api.Infrastructure.Security;

/// <summary>
/// Issuer/Audience/expiraciones vienen de appsettings.json (no sensibles).
/// Key se lee por separado desde dotnet user-secrets / variables de entorno — nunca de appsettings.json.
/// </summary>
public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int AccessTokenExpirationMinutes { get; set; } = 15;
    public int RefreshTokenExpirationDays { get; set; } = 30;
}
