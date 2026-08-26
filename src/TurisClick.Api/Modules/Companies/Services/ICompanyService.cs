using TurisClick.Api.Modules.Companies.Dtos;
using TurisClick.Api.Modules.Companies.Entities;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Companies.Services;

public interface ICompanyService
{
    /// <summary>UC-P-01.</summary>
    Task<RegisterProviderResponse> RegisterProviderAsync(RegisterProviderRequest request, CancellationToken ct);

    /// <summary>UC-A-01. status = null lista todas las empresas, sin filtrar.</summary>
    Task<PagedResult<CompanyResponse>> ListAsync(CompanyStatus? status, string? search, int page, int pageSize, CancellationToken ct);

    /// <summary>Detalle de una empresa para ADMIN (complementa UC-A-01).</summary>
    Task<CompanyResponse> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>UC-A-02.</summary>
    Task<CompanyResponse> ApproveAsync(Guid companyId, Guid adminUserId, CancellationToken ct);

    /// <summary>UC-A-03.</summary>
    Task<CompanyResponse> RejectAsync(Guid companyId, Guid adminUserId, RejectCompanyRequest request, CancellationToken ct);

    /// <summary>UC-P-02 (lectura) — la empresa del PROVIDER autenticado.</summary>
    Task<CompanyResponse> GetMyCompanyAsync(Guid companyId, CancellationToken ct);

    /// <summary>UC-P-02 (edición) — requiere Status = APPROVED.</summary>
    Task<CompanyResponse> UpdateMyCompanyAsync(Guid companyId, UpdateCompanyRequest request, CancellationToken ct);
}
