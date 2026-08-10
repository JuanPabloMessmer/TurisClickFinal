using TurisClick.Api.Modules.Auth.Entities;

namespace TurisClick.Api.Modules.Auth.Services;

public interface ITokenService
{
    string GenerateAccessToken(User user);

    /// <summary>Valor aleatorio opaco (no JWT) — se guarda hasheado, nunca en texto plano.</summary>
    string GenerateRefreshToken();

    /// <summary>Hash rápido (SHA-256) para lookup por índice — un refresh token ya es alta entropía, no necesita PBKDF2.</summary>
    string HashRefreshToken(string refreshToken);

    DateTimeOffset GetAccessTokenExpiration();
    DateTimeOffset GetRefreshTokenExpiration();
}
