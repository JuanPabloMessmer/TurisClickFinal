using TurisClick.Api.Modules.Users;

namespace TurisClick.Api.Modules.Providers.Repositories;

public interface IProviderRepository
{
    Task<IReadOnlyList<Provider>> GetAllAsync();
    Task<Provider?> GetByIdAsync(int providerId);
    Task<Provider?> GetByUserIdAsync(int userId);
    Task<User?> GetUserByIdAsync(int userId);
    Task<bool> UserHasProviderAsync(int userId, int? excludedProviderId = null);
    Task<bool> NitExistsAsync(string nit, int? excludedProviderId = null);
    Task<Provider> CreateAsync(Provider provider);
    Task SaveChangesAsync();
}
