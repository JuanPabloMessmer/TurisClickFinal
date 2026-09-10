using TurisClick.Api.Modules.Admin.Services;

namespace TurisClick.Api.Modules.Admin;

/// <summary>
/// UC-A-06/07/08 (Oleada 8). Las acciones de admin sobre empresas que ya existían (aprobar/rechazar,
/// UC-A-01/02/03) siguen en el módulo Companies — acá solo vive lo que no tenía dueño: cuentas y
/// sanciones de contenido.
/// </summary>
public static class AdminModuleExtensions
{
    public static IServiceCollection AddAdminModule(this IServiceCollection services)
    {
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<IAdminContentService, AdminContentService>();
        return services;
    }
}
