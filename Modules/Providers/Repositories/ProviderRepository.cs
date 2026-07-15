using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Roles;
using TurisClick.Api.Modules.Users;

namespace TurisClick.Api.Modules.Providers.Repositories;

public class ProviderRepository : IProviderRepository
{
    private readonly AppDbContext _dbContext;

    public ProviderRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Provider>> GetAllAsync()
    {
        return await _dbContext.Providers
            .AsNoTracking()
            .Include(provider => provider.User)
            .ThenInclude(user => user.Role)
            .OrderBy(provider => provider.ProviderId)
            .ToListAsync();
    }

    public async Task<Provider?> GetByIdAsync(int providerId)
    {
        return await _dbContext.Providers
            .Include(provider => provider.User)
            .ThenInclude(user => user.Role)
            .FirstOrDefaultAsync(provider => provider.ProviderId == providerId);
    }

    public async Task<Provider?> GetByUserIdAsync(int userId)
    {
        return await _dbContext.Providers
            .Include(provider => provider.User)
            .ThenInclude(user => user.Role)
            .FirstOrDefaultAsync(provider => provider.UserId == userId);
    }

    public async Task<User?> GetUserByIdAsync(int userId)
    {
        return await _dbContext.Users
            .Include(user => user.Role)
            .FirstOrDefaultAsync(user => user.UserId == userId);
    }

    public async Task<Role?> GetRoleByNameAsync(string roleName)
    {
        return await _dbContext.Roles
            .FirstOrDefaultAsync(role => role.Name == roleName);
    }

    public async Task<bool> EmailExistsAsync(string email)
    {
        return await _dbContext.Users.AnyAsync(user => user.Email == email);
    }

    public async Task<bool> UserHasProviderAsync(int userId, int? excludedProviderId = null)
    {
        return await _dbContext.Providers.AnyAsync(provider =>
            provider.UserId == userId &&
            (!excludedProviderId.HasValue || provider.ProviderId != excludedProviderId.Value));
    }

    public async Task<bool> NitExistsAsync(string nit, int? excludedProviderId = null)
    {
        return await _dbContext.Providers.AnyAsync(provider =>
            provider.Nit == nit &&
            (!excludedProviderId.HasValue || provider.ProviderId != excludedProviderId.Value));
    }

    public async Task<Provider> CreateAsync(Provider provider)
    {
        _dbContext.Providers.Add(provider);
        await _dbContext.SaveChangesAsync();

        return provider;
    }

    public async Task<Provider> CreateWithUserAsync(User user, Provider provider)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        provider.UserId = user.UserId;
        provider.User = user;

        _dbContext.Providers.Add(provider);
        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        return provider;
    }

    public async Task SaveChangesAsync()
    {
        await _dbContext.SaveChangesAsync();
    }
}
