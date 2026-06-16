using TurisClick.Api.Modules.Auth.DTOs;

namespace TurisClick.Api.Modules.Auth.Services;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
}
