using TurisClick.Api.Modules.Auth.Repositories;
using TurisClick.Api.Modules.Auth.Services;

namespace TurisClick.Api.Modules.Auth;

public static class AuthModuleExtensions
{
    public static IServiceCollection AddAuthModule(this IServiceCollection services)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ITokenService, JwtTokenService>();
        return services;
    }
}
