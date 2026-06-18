using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Roles;

namespace TurisClick.Api.Modules.Users.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _dbContext;

    public UserRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<User>> GetAllAsync()
    {
        return await _dbContext.Users
            .AsNoTracking()
            .Include(user => user.Role)
            .OrderBy(user => user.UserId)
            .ToListAsync();
    }

    public async Task<User?> GetByIdAsync(int userId)
    {
        return await _dbContext.Users
            .Include(user => user.Role)
            .FirstOrDefaultAsync(user => user.UserId == userId);
    }

    public async Task<bool> EmailExistsAsync(string email, int? excludedUserId = null)
    {
        return await _dbContext.Users.AnyAsync(user =>
            user.Email == email &&
            (!excludedUserId.HasValue || user.UserId != excludedUserId.Value));
    }

    public async Task<Role?> GetRoleByNameAsync(string roleName)
    {
        return await _dbContext.Roles
            .FirstOrDefaultAsync(role => role.Name == roleName);
    }

    public async Task<User> CreateAsync(User user)
    {
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        return user;
    }

    public async Task SaveChangesAsync()
    {
        await _dbContext.SaveChangesAsync();
    }
}
