using TurisClick.Api.Modules.Companies.Repositories;
using TurisClick.Api.Modules.Companies.Services;

namespace TurisClick.Api.Modules.Companies;

public static class CompaniesModuleExtensions
{
    public static IServiceCollection AddCompaniesModule(this IServiceCollection services)
    {
        services.AddScoped<ICompanyRepository, CompanyRepository>();
        services.AddScoped<ICompanyService, CompanyService>();
        return services;
    }
}
