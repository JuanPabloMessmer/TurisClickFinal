using TurisClick.Api.Modules.Auth.Entities;

namespace TurisClick.Api.Modules.Auth.Repositories;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct);
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<bool> EmailExistsAsync(string email, CancellationToken ct);
    Task AddAsync(User user, CancellationToken ct);
}
