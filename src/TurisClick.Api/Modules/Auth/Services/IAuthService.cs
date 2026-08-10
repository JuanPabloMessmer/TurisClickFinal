using TurisClick.Api.Modules.Auth.Dtos;

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
}
