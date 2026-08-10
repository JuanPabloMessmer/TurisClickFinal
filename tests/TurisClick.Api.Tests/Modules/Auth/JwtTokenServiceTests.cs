using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Auth.Entities;
using TurisClick.Api.Modules.Auth.Services;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Auth;

public class JwtTokenServiceTests
{
    private readonly JwtTokenService _sut = new(Options.Create(new JwtSettings
    {
        Key = "unit-test-signing-key-at-least-32-bytes-long-0123456789",
        Issuer = "TurisClick.Api.Tests",
        Audience = "TurisClick.Client.Tests",
        AccessTokenExpirationMinutes = 15,
        RefreshTokenExpirationDays = 30
    }));

    [Fact]
    public void GenerateAccessToken_IncludesSubEmailAndRoleClaims_ButNotCompanyId_WhenTouristHasNoCompany()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "turista@turisclick.dev", Role = UserRole.TOURIST, CompanyId = null };

        var jwt = _sut.GenerateAccessToken(user);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);

        Assert.Equal(user.Id.ToString(), token.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(user.Email, token.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal("TOURIST", token.Claims.First(c => c.Type == ClaimTypes.Role).Value);
        Assert.DoesNotContain(token.Claims, c => c.Type == "company_id");
    }

    [Fact]
    public void GenerateAccessToken_IncludesCompanyId_WhenUserIsProvider()
    {
        var companyId = Guid.NewGuid();
        var user = new User { Id = Guid.NewGuid(), Email = "proveedor@turisclick.dev", Role = UserRole.PROVIDER, CompanyId = companyId };

        var jwt = _sut.GenerateAccessToken(user);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);

        Assert.Equal(companyId.ToString(), token.Claims.First(c => c.Type == "company_id").Value);
    }

    [Fact]
    public void HashRefreshToken_IsDeterministic_ForTheSameInput()
    {
        var hash1 = _sut.HashRefreshToken("mismo-valor");
        var hash2 = _sut.HashRefreshToken("mismo-valor");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void HashRefreshToken_DiffersForDifferentInputs()
    {
        var hash1 = _sut.HashRefreshToken("valor-a");
        var hash2 = _sut.HashRefreshToken("valor-b");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void GenerateRefreshToken_ProducesHighEntropyUniqueValues()
    {
        var first = _sut.GenerateRefreshToken();
        var second = _sut.GenerateRefreshToken();

        Assert.NotEqual(first, second);
        Assert.True(first.Length > 40);
    }
}
