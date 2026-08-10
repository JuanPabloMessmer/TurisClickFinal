using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Auth.Entities;

namespace TurisClick.Api.Modules.Auth.Services;

public class JwtTokenService(IOptions<JwtSettings> jwtOptions) : ITokenService
{
    private readonly JwtSettings _settings = jwtOptions.Value;

    public string GenerateAccessToken(User user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        // Solo PROVIDER tiene company_id (docs/domain-model.md §1) — UC-SYS-03 lo usará más adelante.
        if (user.CompanyId is { } companyId)
            claims.Add(new Claim("company_id", companyId.ToString()));

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: GetAccessTokenExpiration().UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public string HashRefreshToken(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    public DateTimeOffset GetAccessTokenExpiration() =>
        DateTimeOffset.UtcNow.AddMinutes(_settings.AccessTokenExpirationMinutes);

    public DateTimeOffset GetRefreshTokenExpiration() =>
        DateTimeOffset.UtcNow.AddDays(_settings.RefreshTokenExpirationDays);
}
