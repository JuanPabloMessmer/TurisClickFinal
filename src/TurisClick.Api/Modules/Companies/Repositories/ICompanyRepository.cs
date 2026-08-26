using TurisClick.Api.Modules.Companies.Entities;

namespace TurisClick.Api.Modules.Companies.Repositories;

public interface ICompanyRepository
{
    Task<Company?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<bool> LegalDocumentExistsAsync(string legalDocument, CancellationToken ct);
    Task<(List<Company> Items, int TotalCount)> ListAsync(CompanyStatus? status, string? search, int page, int pageSize, CancellationToken ct);
    Task AddAsync(Company company, CancellationToken ct);
}
