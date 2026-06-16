using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Roles;
using TurisClick.Api.Modules.Users;

namespace TurisClick.Api.Modules.Auth.Repositories;

public class AuthRepository : IAuthRepository
{
    private readonly AppDbContext _dbContext;

    public AuthRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<User?> GetUserByEmailAsync(string email)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        return await _dbContext.Users
            .Include(user => user.Role)
            .FirstOrDefaultAsync(user => user.Email.ToLower() == normalizedEmail);
    }

    public async Task<Role?> GetRoleByNameAsync(string name)
    {
        var normalizedName = name.Trim().ToUpperInvariant();

        return await _dbContext.Roles
            .FirstOrDefaultAsync(role => role.Name == normalizedName);
    }

    public async Task<User> CreateUserAsync(User user)
    {
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        return user;
    }
}
