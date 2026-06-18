using TurisClick.Api.Modules.Roles;

namespace TurisClick.Api.Modules.Users.Repositories;

public interface IUserRepository
{
    Task<IReadOnlyList<User>> GetAllAsync();
    Task<User?> GetByIdAsync(int userId);
    Task<bool> EmailExistsAsync(string email, int? excludedUserId = null);
    Task<Role?> GetRoleByNameAsync(string roleName);
    Task<User> CreateAsync(User user);
    Task SaveChangesAsync();
}
