using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TurisClick.Api.Modules.Users.DTOs;
using TurisClick.Api.Modules.Users.Repositories;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Users.Services;

public class UserService : IUserService
{
    private static readonly HashSet<string> AllowedRoles =
        new(StringComparer.OrdinalIgnoreCase) { "ADMIN", "TOURIST", "PROVIDER" };

    private static readonly HashSet<string> AllowedStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "ACTIVE", "INACTIVE", "BLOCKED" };

    private readonly IUserRepository _userRepository;
    private readonly PasswordHasher<User> _passwordHasher;

    public UserService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
        _passwordHasher = new PasswordHasher<User>();
    }

    public async Task<IReadOnlyList<UserResponse>> GetAllAsync()
    {
        var users = await _userRepository.GetAllAsync();
        return users.Select(MapToResponse).ToList();
    }

    public async Task<UserResponse> GetByIdAsync(int userId)
    {
        var user = await GetRequiredUserAsync(userId);
        return MapToResponse(user);
    }

    public async Task<UserResponse> CreateAsync(CreateUserRequest request)
    {
        var email = NormalizeEmail(request.Email);

        if (await _userRepository.EmailExistsAsync(email))
        {
            throw new ConflictException("Email is already registered.");
        }

        var roleName = NormalizeRole(request.Role);
        var role = await GetRequiredRoleAsync(roleName);

        var user = new User
        {
            RoleId = role.RoleId,
            FirstName = request.FirstName.Trim(),
            LastName = NormalizeOptional(request.LastName),
            Email = email,
            Phone = NormalizeOptional(request.Phone),
            Status = "ACTIVE",
            CreatedAt = DateTime.UtcNow,
            Role = role
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        try
        {
            await _userRepository.CreateAsync(user);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new ConflictException("Email is already registered.");
        }

        return MapToResponse(user);
    }

    public async Task<UserResponse> UpdateAsync(int userId, UpdateUserRequest request)
    {
        var user = await GetRequiredUserAsync(userId);
        var email = NormalizeEmail(request.Email);

        if (await _userRepository.EmailExistsAsync(email, userId))
        {
            throw new ConflictException("Email is already registered.");
        }

        user.FirstName = request.FirstName.Trim();
        user.LastName = NormalizeOptional(request.LastName);
        user.Email = email;
        user.Phone = NormalizeOptional(request.Phone);

        try
        {
            await _userRepository.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new ConflictException("Email is already registered.");
        }

        return MapToResponse(user);
    }

    public async Task<UserResponse> ChangeStatusAsync(int userId, ChangeUserStatusRequest request)
    {
        var user = await GetRequiredUserAsync(userId);
        var status = request.Status.Trim().ToUpperInvariant();

        if (!AllowedStatuses.Contains(status))
        {
            throw new ValidationException("Status must be ACTIVE, INACTIVE, or BLOCKED.");
        }

        user.Status = status;
        await _userRepository.SaveChangesAsync();

        return MapToResponse(user);
    }

    public async Task<UserResponse> ChangeRoleAsync(int userId, ChangeUserRoleRequest request)
    {
        var user = await GetRequiredUserAsync(userId);
        var roleName = NormalizeRole(request.Role);
        var role = await GetRequiredRoleAsync(roleName);

        user.RoleId = role.RoleId;
        user.Role = role;
        await _userRepository.SaveChangesAsync();

        return MapToResponse(user);
    }

    private async Task<User> GetRequiredUserAsync(int userId)
    {
        return await _userRepository.GetByIdAsync(userId)
            ?? throw new NotFoundException("User was not found.");
    }

    private async Task<Roles.Role> GetRequiredRoleAsync(string roleName)
    {
        return await _userRepository.GetRoleByNameAsync(roleName)
            ?? throw new NotFoundException("Role was not found.");
    }

    private static string NormalizeRole(string role)
    {
        var normalizedRole = role.Trim().ToUpperInvariant();

        if (!AllowedRoles.Contains(normalizedRole))
        {
            throw new ValidationException("Role must be ADMIN, TOURIST, or PROVIDER.");
        }

        return normalizedRole;
    }

    private static UserResponse MapToResponse(User user)
    {
        return new UserResponse
        {
            UserId = user.UserId,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            Phone = user.Phone,
            Status = user.Status,
            Role = user.Role.Name,
            CreatedAt = user.CreatedAt
        };
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        };
    }
}
