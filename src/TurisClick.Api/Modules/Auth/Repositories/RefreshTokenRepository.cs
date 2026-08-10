using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Auth.Entities;

namespace TurisClick.Api.Modules.Auth.Repositories;

public class RefreshTokenRepository(TurisClickDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken ct) =>
        db.RefreshTokens.Include(rt => rt.User).FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, ct);

    public async Task AddAsync(RefreshToken refreshToken, CancellationToken ct) =>
        await db.RefreshTokens.AddAsync(refreshToken, ct);
}
