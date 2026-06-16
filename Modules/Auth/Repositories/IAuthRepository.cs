using TurisClick.Api.Modules.Roles;
using TurisClick.Api.Modules.Users;

namespace TurisClick.Api.Modules.Auth.Repositories;

public interface IAuthRepository
{
    Task<User?> GetUserByEmailAsync(string email);
    Task<Role?> GetRoleByNameAsync(string name);
    Task<User> CreateUserAsync(User user);
}
