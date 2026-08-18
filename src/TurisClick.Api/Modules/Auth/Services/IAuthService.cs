using TurisClick.Api.Modules.Auth.Dtos;
using TurisClick.Api.Modules.Auth.Entities;

namespace TurisClick.Api.Modules.Auth.Services;

public interface IAuthService
{
    /// <summary>UC-AUTH-01.</summary>
    Task<AuthResultResponse> RegisterTouristAsync(RegisterTouristRequest request, CancellationToken ct);

    /// <summary>UC-AUTH-02.</summary>
    Task<AuthResultResponse> LoginAsync(LoginRequest request, CancellationToken ct);

    /// <summary>UC-AUTH-03.</summary>
    Task<AuthResultResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken ct);

    /// <summary>UC-AUTH-04.</summary>
    Task LogoutAsync(Guid currentUserId, LogoutRequest request, CancellationToken ct);

    /// <summary>
    /// Emite access token + refresh token para un User ya persistido (o pendiente de SaveChanges en el
    /// mismo DbContext scoped). Reutilizado por UC-P-01 (registrar Provider) para no duplicar la lógica
    /// de emisión/hasheo de refresh token — no llama a SaveChangesAsync, eso queda a cargo del caller.
    /// </summary>
    Task<AuthResultResponse> IssueTokensForUserAsync(User user, CancellationToken ct);
}
