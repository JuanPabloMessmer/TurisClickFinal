using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Auth.Entities;

namespace TurisClick.Api.Modules.Auth.Repositories;

/// <summary>Solo queries — sin reglas de negocio (ver docs/backend-architecture.md, punto 1-2).</summary>
public class UserRepository(TurisClickDbContext db) : IUserRepository
{
    public Task<User?> GetByEmailAsync(string email, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.Email == email, ct);

    public async Task AddAsync(User user, CancellationToken ct) =>
        await db.Users.AddAsync(user, ct);
}
