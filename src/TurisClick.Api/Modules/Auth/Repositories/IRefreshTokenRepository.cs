using TurisClick.Api.Modules.Auth.Entities;

namespace TurisClick.Api.Modules.Auth.Repositories;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken ct);
    Task AddAsync(RefreshToken refreshToken, CancellationToken ct);
}
