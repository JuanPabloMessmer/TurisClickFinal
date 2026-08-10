namespace TurisClick.Api.Infrastructure.Security;

public static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddSecurityInfrastructure(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        services.AddScoped<ICompanyOwnershipGuard, CompanyOwnershipGuard>();
        services.AddSingleton<IPasswordHasherService, PasswordHasherService>();
        return services;
    }
}
