using Microsoft.EntityFrameworkCore;
using Moq;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Auth.Dtos;
using TurisClick.Api.Modules.Auth.Entities;
using TurisClick.Api.Modules.Auth.Repositories;
using TurisClick.Api.Modules.Auth.Services;
using TurisClick.Api.Shared.Exceptions;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Auth;

/// <summary>
/// Tests unitarios de AuthService (UC-AUTH-01..04) con repositorios/servicios mockeados —
/// sin tocar Postgres. Los tests de integración (Integration/AuthEndpointsTests.cs) cubren
/// el pipeline HTTP + EF Core real.
/// </summary>
public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepository = new();
    private readonly Mock<IPasswordHasherService> _passwordHasher = new();
    private readonly Mock<ITokenService> _tokenService = new();
    private readonly Mock<TurisClickDbContext> _db;
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        var options = new DbContextOptionsBuilder<TurisClickDbContext>().Options;
        _db = new Mock<TurisClickDbContext>(options);
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _tokenService.Setup(t => t.GenerateAccessToken(It.IsAny<User>())).Returns("fake-access-token");
        _tokenService.Setup(t => t.GenerateRefreshToken()).Returns("fake-refresh-token-plain");
        _tokenService.Setup(t => t.HashRefreshToken(It.IsAny<string>())).Returns<string>(s => $"hash({s})");
        _tokenService.Setup(t => t.GetAccessTokenExpiration()).Returns(DateTimeOffset.UtcNow.AddMinutes(15));
        _tokenService.Setup(t => t.GetRefreshTokenExpiration()).Returns(DateTimeOffset.UtcNow.AddDays(30));

        _sut = new AuthService(
            _userRepository.Object,
            _refreshTokenRepository.Object,
            _passwordHasher.Object,
            _tokenService.Object,
            _db.Object);
    }

    [Fact]
    public async Task RegisterTouristAsync_WithNewEmail_CreatesTouristAndReturnsTokens()
    {
        _userRepository.Setup(r => r.EmailExistsAsync("nuevo@turisclick.dev", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _passwordHasher.Setup(p => p.Hash("Password123!")).Returns("hashed-password");

        var request = new RegisterTouristRequest { FirstName = "Ana", LastName = "Pérez", Email = "nuevo@turisclick.dev", Password = "Password123!" };

        var result = await _sut.RegisterTouristAsync(request, CancellationToken.None);

        Assert.Equal("fake-access-token", result.AccessToken);
        Assert.Equal("fake-refresh-token-plain", result.RefreshToken);
        Assert.Equal("TOURIST", result.User.Role);
        Assert.Equal("nuevo@turisclick.dev", result.User.Email);
        Assert.Equal("Ana", result.User.FirstName);
        Assert.Equal("Pérez", result.User.LastName);
        Assert.Equal("Ana Pérez", result.User.FullName);

        _userRepository.Verify(r => r.AddAsync(
            It.Is<User>(u => u.Email == "nuevo@turisclick.dev" && u.Role == UserRole.TOURIST && u.CompanyId == null
                && u.FirstName == "Ana" && u.LastName == "Pérez"),
            It.IsAny<CancellationToken>()), Times.Once);
        _refreshTokenRepository.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterTouristAsync_WithExistingEmail_ThrowsConflict()
    {
        _userRepository.Setup(r => r.EmailExistsAsync("ya-existe@turisclick.dev", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new RegisterTouristRequest { FirstName = "Ana", LastName = "Pérez", Email = "ya-existe@turisclick.dev", Password = "Password123!" };

        await Assert.ThrowsAsync<ConflictAppException>(() => _sut.RegisterTouristAsync(request, CancellationToken.None));
        _userRepository.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsTokens()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "turista@turisclick.dev", PasswordHash = "hashed", Role = UserRole.TOURIST, Status = UserStatus.ACTIVE };
        _userRepository.Setup(r => r.GetByEmailAsync("turista@turisclick.dev", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasher.Setup(p => p.Verify("hashed", "correcta")).Returns(true);

        var result = await _sut.LoginAsync(new LoginRequest { Email = "turista@turisclick.dev", Password = "correcta" }, CancellationToken.None);

        Assert.Equal("fake-access-token", result.AccessToken);
    }

    [Fact]
    public async Task LoginAsync_WithUnknownEmail_ThrowsUnauthorized()
    {
        _userRepository.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            _sut.LoginAsync(new LoginRequest { Email = "nadie@turisclick.dev", Password = "x" }, CancellationToken.None));
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ThrowsUnauthorized()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "turista@turisclick.dev", PasswordHash = "hashed", Role = UserRole.TOURIST, Status = UserStatus.ACTIVE };
        _userRepository.Setup(r => r.GetByEmailAsync("turista@turisclick.dev", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasher.Setup(p => p.Verify("hashed", "incorrecta")).Returns(false);

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            _sut.LoginAsync(new LoginRequest { Email = "turista@turisclick.dev", Password = "incorrecta" }, CancellationToken.None));
    }

    [Fact]
    public async Task LoginAsync_WithSuspendedUser_ThrowsForbidden()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "suspendido@turisclick.dev", PasswordHash = "hashed", Role = UserRole.TOURIST, Status = UserStatus.SUSPENDED };
        _userRepository.Setup(r => r.GetByEmailAsync("suspendido@turisclick.dev", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasher.Setup(p => p.Verify("hashed", "correcta")).Returns(true);

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            _sut.LoginAsync(new LoginRequest { Email = "suspendido@turisclick.dev", Password = "correcta" }, CancellationToken.None));
    }

    [Fact]
    public async Task RefreshAsync_WithActiveToken_RotatesAndReturnsNewTokens()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "turista@turisclick.dev", Role = UserRole.TOURIST, Status = UserStatus.ACTIVE };
        var existingToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            TokenHash = "hash(valid-refresh-token)",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(10),
            RevokedAt = null
        };
        _tokenService.Setup(t => t.HashRefreshToken("valid-refresh-token")).Returns("hash(valid-refresh-token)");
        _refreshTokenRepository.Setup(r => r.GetByTokenHashAsync("hash(valid-refresh-token)", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingToken);

        var result = await _sut.RefreshAsync(new RefreshTokenRequest { RefreshToken = "valid-refresh-token" }, CancellationToken.None);

        Assert.Equal("fake-access-token", result.AccessToken);
        Assert.NotNull(existingToken.RevokedAt); // rotación: el token usado queda revocado
    }

    [Fact]
    public async Task RefreshAsync_WithExpiredToken_ThrowsUnauthorized()
    {
        var expired = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            User = new User { Id = Guid.NewGuid(), Role = UserRole.TOURIST, Status = UserStatus.ACTIVE },
            TokenHash = "hash(expired-token)",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        _tokenService.Setup(t => t.HashRefreshToken("expired-token")).Returns("hash(expired-token)");
        _refreshTokenRepository.Setup(r => r.GetByTokenHashAsync("hash(expired-token)", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expired);

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            _sut.RefreshAsync(new RefreshTokenRequest { RefreshToken = "expired-token" }, CancellationToken.None));
    }

    [Fact]
    public async Task RefreshAsync_WithRevokedToken_ThrowsUnauthorized()
    {
        var revoked = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            User = new User { Id = Guid.NewGuid(), Role = UserRole.TOURIST, Status = UserStatus.ACTIVE },
            TokenHash = "hash(revoked-token)",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(10),
            RevokedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        _tokenService.Setup(t => t.HashRefreshToken("revoked-token")).Returns("hash(revoked-token)");
        _refreshTokenRepository.Setup(r => r.GetByTokenHashAsync("hash(revoked-token)", It.IsAny<CancellationToken>()))
            .ReturnsAsync(revoked);

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            _sut.RefreshAsync(new RefreshTokenRequest { RefreshToken = "revoked-token" }, CancellationToken.None));
    }

    [Fact]
    public async Task LogoutAsync_WithOwnToken_RevokesIt()
    {
        var userId = Guid.NewGuid();
        var token = new RefreshToken { Id = Guid.NewGuid(), UserId = userId, TokenHash = "hash(mine)", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) };
        _tokenService.Setup(t => t.HashRefreshToken("mine")).Returns("hash(mine)");
        _refreshTokenRepository.Setup(r => r.GetByTokenHashAsync("hash(mine)", It.IsAny<CancellationToken>())).ReturnsAsync(token);

        await _sut.LogoutAsync(userId, new LogoutRequest { RefreshToken = "mine" }, CancellationToken.None);

        Assert.NotNull(token.RevokedAt);
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task LogoutAsync_WithTokenBelongingToAnotherUser_ThrowsNotFound()
    {
        var token = new RefreshToken { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), TokenHash = "hash(other)", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) };
        _tokenService.Setup(t => t.HashRefreshToken("other")).Returns("hash(other)");
        _refreshTokenRepository.Setup(r => r.GetByTokenHashAsync("hash(other)", It.IsAny<CancellationToken>())).ReturnsAsync(token);

        await Assert.ThrowsAsync<NotFoundAppException>(() =>
            _sut.LogoutAsync(Guid.NewGuid(), new LogoutRequest { RefreshToken = "other" }, CancellationToken.None));
    }

    [Fact]
    public async Task LogoutAsync_WithUnknownToken_ThrowsNotFound()
    {
        _tokenService.Setup(t => t.HashRefreshToken("desconocido")).Returns("hash(desconocido)");
        _refreshTokenRepository.Setup(r => r.GetByTokenHashAsync("hash(desconocido)", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshToken?)null);

        await Assert.ThrowsAsync<NotFoundAppException>(() =>
            _sut.LogoutAsync(Guid.NewGuid(), new LogoutRequest { RefreshToken = "desconocido" }, CancellationToken.None));
    }
}
