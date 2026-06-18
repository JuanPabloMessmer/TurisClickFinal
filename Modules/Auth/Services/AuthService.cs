using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using TurisClick.Api.Modules.Auth.DTOs;
using TurisClick.Api.Modules.Auth.Repositories;
using TurisClick.Api.Modules.Users;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Auth.Services;

public class AuthService : IAuthService
{
    private const string DefaultUserRole = "TOURIST";
    private const string ActiveStatus = "ACTIVE";
    private static readonly HashSet<string> AppRoles =
        new(StringComparer.OrdinalIgnoreCase) { "TOURIST" };

    private static readonly HashSet<string> BackofficeRoles =
        new(StringComparer.OrdinalIgnoreCase) { "ADMIN", "PROVIDER" };

    private readonly IAuthRepository _authRepository;
    private readonly IConfiguration _configuration;
    private readonly PasswordHasher<User> _passwordHasher;

    public AuthService(IAuthRepository authRepository, IConfiguration configuration)
    {
        _authRepository = authRepository;
        _configuration = configuration;
        _passwordHasher = new PasswordHasher<User>();
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        var email = NormalizeEmail(request.Email);
        var existingUser = await _authRepository.GetUserByEmailAsync(email);

        if (existingUser is not null)
        {
            throw new ConflictException("Email is already registered.");
        }

        var role = await _authRepository.GetRoleByNameAsync(DefaultUserRole);

        if (role is null)
        {
            throw new NotFoundException("Default user role was not found.");
        }

        var user = new User
        {
            RoleId = role.RoleId,
            FirstName = request.FirstName.Trim(),
            LastName = string.IsNullOrWhiteSpace(request.LastName) ? null : request.LastName.Trim(),
            Email = email,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Status = ActiveStatus,
            CreatedAt = DateTime.UtcNow,
            Role = role
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        var createdUser = await _authRepository.CreateUserAsync(user);
        createdUser.Role = role;

        return BuildAuthResponse(createdUser);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        return await AuthenticateAsync(request, AppRoles, "User cannot access the tourist application.");
    }

    public async Task<AuthResponse> LoginBackofficeAsync(LoginRequest request)
    {
        return await AuthenticateAsync(request, BackofficeRoles, "User cannot access the backoffice.");
    }

    private async Task<AuthResponse> AuthenticateAsync(
        LoginRequest request,
        IReadOnlySet<string> allowedRoles,
        string unauthorizedRoleMessage)
    {
        var email = NormalizeEmail(request.Email);
        var user = await _authRepository.GetUserByEmailAsync(email);

        if (user is null)
        {
            throw new UnauthorizedException("Invalid credentials.");
        }

        if (!string.Equals(user.Status, ActiveStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("User is inactive.");
        }

        var passwordResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (passwordResult == PasswordVerificationResult.Failed)
        {
            throw new UnauthorizedException("Invalid credentials.");
        }

        if (user.Role is null)
        {
            throw new NotFoundException("User role was not found.");
        }

        if (!allowedRoles.Contains(user.Role.Name))
        {
            throw new ForbiddenException(unauthorizedRoleMessage);
        }

        return BuildAuthResponse(user);
    }

    private AuthResponse BuildAuthResponse(User user)
    {
        return new AuthResponse
        {
            Token = GenerateJwt(user),
            UserId = user.UserId,
            Name = BuildFullName(user),
            Email = user.Email,
            Role = user.Role.Name
        };
    }

    private string GenerateJwt(User user)
    {
        var jwtSection = _configuration.GetSection("Jwt");
        var key = jwtSection["Key"];

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("JWT key is not configured.");
        }

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var expiresInMinutes = int.TryParse(jwtSection["ExpiresInMinutes"], out var minutes) ? minutes : 120;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new("user_id", user.UserId.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.Name),
            new("role", user.Role.Name)
        };

        var token = new JwtSecurityToken(
            issuer: jwtSection["Issuer"],
            audience: jwtSection["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiresInMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string BuildFullName(User user)
    {
        return string.IsNullOrWhiteSpace(user.LastName)
            ? user.FirstName
            : $"{user.FirstName} {user.LastName}";
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }
}
