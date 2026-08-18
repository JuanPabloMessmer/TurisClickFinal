using TurisClick.Api.Modules.Categories.Repositories;
using TurisClick.Api.Modules.Categories.Services;

namespace TurisClick.Api.Modules.Categories;

public static class CategoriesModuleExtensions
{
    public static IServiceCollection AddCategoriesModule(this IServiceCollection services)
    {
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ICategoryService, CategoryService>();
        return services;
    }
}
