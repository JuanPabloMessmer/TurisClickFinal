using TurisClick.Api.Modules.Companies.Entities;

namespace TurisClick.Api.Modules.Companies.Repositories;

public interface ICompanyRepository
{
    Task<Company?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<(List<Company> Items, int TotalCount)> ListAsync(CompanyStatus? status, string? search, int page, int pageSize, CancellationToken ct);
    Task AddAsync(Company company, CancellationToken ct);

    /// <summary>Empresa distinta de <paramref name="excludeId"/> con la misma clave de nombre K1, si existe.</summary>
    Task<CompanyNameMatch?> FindNameConflictAsync(string nameKey, Guid? excludeId, CancellationToken ct);

    /// <summary>Empresas con un nombre parecido (misma K2, distinta K1). Sólo informa: no bloquea.</summary>
    Task<List<CompanyNameMatch>> FindSimilarAsync(string name, Guid? excludeId, CancellationToken ct);
}
