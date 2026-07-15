using TurisClick.Api.Modules.Users;
using TurisClick.Api.Modules.Roles;

namespace TurisClick.Api.Modules.Providers.Repositories;

public interface IProviderRepository
{
    Task<IReadOnlyList<Provider>> GetAllAsync();
    Task<Provider?> GetByIdAsync(int providerId);
    Task<Provider?> GetByUserIdAsync(int userId);
    Task<User?> GetUserByIdAsync(int userId);
    Task<Role?> GetRoleByNameAsync(string roleName);
    Task<bool> EmailExistsAsync(string email);
    Task<bool> UserHasProviderAsync(int userId, int? excludedProviderId = null);
    Task<bool> NitExistsAsync(string nit, int? excludedProviderId = null);
    Task<Provider> CreateAsync(Provider provider);
    Task<Provider> CreateWithUserAsync(User user, Provider provider);
    Task SaveChangesAsync();
}
