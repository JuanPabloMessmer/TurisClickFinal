using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TurisClick.Api.Modules.Providers.DTOs;
using TurisClick.Api.Modules.Providers.Repositories;
using TurisClick.Api.Modules.Users;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Providers.Services;

public class ProviderService : IProviderService
{
    private static readonly HashSet<string> AllowedStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "ACTIVE", "INACTIVE", "BLOCKED" };

    private readonly IProviderRepository _providerRepository;
    private readonly PasswordHasher<User> _passwordHasher;

    public ProviderService(IProviderRepository providerRepository)
    {
        _providerRepository = providerRepository;
        _passwordHasher = new PasswordHasher<User>();
    }

    public async Task<IReadOnlyList<ProviderResponse>> GetAllAsync()
    {
        var providers = await _providerRepository.GetAllAsync();
        return providers.Select(MapToResponse).ToList();
    }

    public async Task<ProviderResponse> GetByIdAsync(int providerId)
    {
        var provider = await GetRequiredProviderAsync(providerId);
        return MapToResponse(provider);
    }

    public async Task<ProviderResponse> CreateAsync(CreateProviderRequest request)
    {
        var email = NormalizeEmail(request.Email);

        if (await _providerRepository.EmailExistsAsync(email))
        {
            throw new ConflictException("Email is already registered.");
        }

        var nit = NormalizeOptional(request.Nit);

        if (nit is not null && await _providerRepository.NitExistsAsync(nit))
        {
            throw new ConflictException("NIT is already registered.");
        }

        var role = await _providerRepository.GetRoleByNameAsync("PROVIDER")
            ?? throw new NotFoundException("Provider role was not found.");

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

        var provider = new Provider
        {
            CompanyName = request.CompanyName.Trim(),
            Nit = nit,
            Description = NormalizeOptional(request.Description),
            ContactPhone = NormalizeOptional(request.ContactPhone),
            ContactEmail = NormalizeEmailOptional(request.ContactEmail),
            Address = NormalizeOptional(request.Address),
            LogoUrl = NormalizeOptional(request.LogoUrl),
            Status = "ACTIVE",
            CreatedAt = DateTime.UtcNow,
            User = user
        };

        try
        {
            await _providerRepository.CreateWithUserAsync(user, provider);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new ConflictException("Email, provider user, or NIT is already registered.");
        }

        return MapToResponse(provider);
    }

    public async Task<ProviderResponse> UpdateAsync(int providerId, UpdateProviderRequest request)
    {
        var provider = await GetRequiredProviderAsync(providerId);
        await ApplyUpdateAsync(provider, request);

        return MapToResponse(provider);
    }

    public async Task<ProviderResponse> ChangeStatusAsync(int providerId, ChangeProviderStatusRequest request)
    {
        var provider = await GetRequiredProviderAsync(providerId);
        var status = request.Status.Trim().ToUpperInvariant();

        if (!AllowedStatuses.Contains(status))
        {
            throw new ValidationException("Status must be ACTIVE, INACTIVE, or BLOCKED.");
        }

        provider.Status = status;
        await _providerRepository.SaveChangesAsync();

        return MapToResponse(provider);
    }

    public async Task<ProviderResponse> GetProfileAsync(int userId)
    {
        var provider = await GetRequiredProviderByUserAsync(userId);
        return MapToResponse(provider);
    }

    public async Task<ProviderResponse> UpdateProfileAsync(int userId, UpdateProviderRequest request)
    {
        var provider = await GetRequiredProviderByUserAsync(userId);
        await ApplyUpdateAsync(provider, request);

        return MapToResponse(provider);
    }

    private async Task ApplyUpdateAsync(Provider provider, UpdateProviderRequest request)
    {
        var nit = NormalizeOptional(request.Nit);

        if (nit is not null && await _providerRepository.NitExistsAsync(nit, provider.ProviderId))
        {
            throw new ConflictException("NIT is already registered.");
        }

        provider.CompanyName = request.CompanyName.Trim();
        provider.Nit = nit;
        provider.Description = NormalizeOptional(request.Description);
        provider.ContactPhone = NormalizeOptional(request.ContactPhone);
        provider.ContactEmail = NormalizeEmailOptional(request.ContactEmail);
        provider.Address = NormalizeOptional(request.Address);
        provider.LogoUrl = NormalizeOptional(request.LogoUrl);

        try
        {
            await _providerRepository.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new ConflictException("NIT is already registered.");
        }
    }

    private async Task<Provider> GetRequiredProviderAsync(int providerId)
    {
        return await _providerRepository.GetByIdAsync(providerId)
            ?? throw new NotFoundException("Provider was not found.");
    }

    private async Task<Provider> GetRequiredProviderByUserAsync(int userId)
    {
        return await _providerRepository.GetByUserIdAsync(userId)
            ?? throw new NotFoundException("Provider profile was not found.");
    }

    private static ProviderResponse MapToResponse(Provider provider)
    {
        return new ProviderResponse
        {
            ProviderId = provider.ProviderId,
            UserId = provider.UserId,
            UserEmail = provider.User.Email,
            UserName = BuildFullName(provider.User),
            CompanyName = provider.CompanyName,
            Nit = provider.Nit,
            Description = provider.Description,
            ContactPhone = provider.ContactPhone,
            ContactEmail = provider.ContactEmail,
            Address = provider.Address,
            LogoUrl = provider.LogoUrl,
            Status = provider.Status,
            CreatedAt = provider.CreatedAt
        };
    }

    private static string BuildFullName(User user)
    {
        return string.IsNullOrWhiteSpace(user.LastName)
            ? user.FirstName
            : $"{user.FirstName} {user.LastName}";
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeEmailOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        };
    }
}
