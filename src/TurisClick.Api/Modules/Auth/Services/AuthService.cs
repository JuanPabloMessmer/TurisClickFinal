using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Auth.Dtos;
using TurisClick.Api.Modules.Auth.Entities;
using TurisClick.Api.Modules.Auth.Repositories;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Auth.Services;

public class AuthService(
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IPasswordHasherService passwordHasher,
    ITokenService tokenService,
    TurisClickDbContext db) : IAuthService
{
    public async Task<AuthResultResponse> RegisterTouristAsync(RegisterTouristRequest request, CancellationToken ct)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        if (await userRepository.EmailExistsAsync(normalizedEmail, ct))
            throw new ConflictAppException("Ya existe una cuenta registrada con este email.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = normalizedEmail,
            PasswordHash = passwordHasher.Hash(request.Password),
            Role = UserRole.TOURIST,
            Status = UserStatus.ACTIVE,
            CompanyId = null,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await userRepository.AddAsync(user, ct);
        var result = await IssueTokensAsync(user, ct);
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<AuthResultResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await userRepository.GetByEmailAsync(normalizedEmail, ct)
            ?? throw new UnauthorizedAppException("Email o contraseña inválidos.");

        if (!passwordHasher.Verify(user.PasswordHash, request.Password))
            throw new UnauthorizedAppException("Email o contraseña inválidos.");

        if (user.Status == UserStatus.SUSPENDED)
            throw new ForbiddenAppException("Esta cuenta está suspendida.");

        var result = await IssueTokensAsync(user, ct);
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<AuthResultResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken ct)
    {
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);
        var existing = await refreshTokenRepository.GetByTokenHashAsync(tokenHash, ct)
            ?? throw new UnauthorizedAppException("Refresh token inválido.");

        if (!existing.IsActive)
            throw new UnauthorizedAppException("Refresh token inválido o expirado.");

        var user = existing.User
            ?? await userRepository.GetByIdAsync(existing.UserId, ct)
            ?? throw new UnauthorizedAppException("Refresh token inválido.");

        if (user.Status == UserStatus.SUSPENDED)
            throw new ForbiddenAppException("Esta cuenta está suspendida.");

        // Rotación: se revoca el refresh token usado y se emite uno nuevo.
        existing.RevokedAt = DateTimeOffset.UtcNow;

        var result = await IssueTokensAsync(user, ct);
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task LogoutAsync(Guid currentUserId, LogoutRequest request, CancellationToken ct)
    {
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);
        var existing = await refreshTokenRepository.GetByTokenHashAsync(tokenHash, ct);

        if (existing is null || existing.UserId != currentUserId)
            throw new NotFoundAppException("Refresh token no encontrado.");

        existing.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task<AuthResultResponse> IssueTokensAsync(User user, CancellationToken ct)
    {
        var accessToken = tokenService.GenerateAccessToken(user);
        var refreshTokenPlain = tokenService.GenerateRefreshToken();

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenService.HashRefreshToken(refreshTokenPlain),
            ExpiresAt = tokenService.GetRefreshTokenExpiration(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        await refreshTokenRepository.AddAsync(refreshToken, ct);

        return new AuthResultResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshTokenPlain,
            ExpiresAtUtc = tokenService.GetAccessTokenExpiration().UtcDateTime,
            User = new UserSummaryResponse
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role.ToString()
            }
        };
    }
}
