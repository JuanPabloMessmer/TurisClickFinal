using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Auth.Entities;
using TurisClick.Api.Modules.Auth.Repositories;
using TurisClick.Api.Modules.Auth.Services;
using TurisClick.Api.Modules.Companies.Dtos;
using TurisClick.Api.Modules.Companies.Entities;
using TurisClick.Api.Modules.Companies.Repositories;
using TurisClick.Api.Shared.Exceptions;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Companies.Services;

public class CompanyService(
    ICompanyRepository companyRepository,
    IUserRepository userRepository,
    IPasswordHasherService passwordHasher,
    IAuthService authService,
    TurisClickDbContext db) : ICompanyService
{
    public async Task<RegisterProviderResponse> RegisterProviderAsync(RegisterProviderRequest request, CancellationToken ct)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        if (await userRepository.EmailExistsAsync(normalizedEmail, ct))
            throw new ConflictAppException("Ya existe una cuenta registrada con este email.");

        var legalDocument = request.LegalDocument.Trim();
        if (await companyRepository.LegalDocumentExistsAsync(legalDocument, ct))
            throw new ConflictAppException("Ya existe una empresa registrada con ese documento legal.");

        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = request.CompanyName.Trim(),
            Description = request.CompanyDescription?.Trim(),
            LegalDocument = legalDocument,
            ContactEmail = request.ContactEmail.Trim().ToLowerInvariant(),
            ContactPhone = request.ContactPhone?.Trim(),
            Status = CompanyStatus.PENDING_APPROVAL,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await companyRepository.AddAsync(company, ct);

        var user = new User
        {
            Id = Guid.NewGuid(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = normalizedEmail,
            PasswordHash = passwordHasher.Hash(request.Password),
            Role = UserRole.PROVIDER,
            Status = UserStatus.ACTIVE,
            CompanyId = company.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await userRepository.AddAsync(user, ct);

        // UC-AUTH-01 ya establece el patrón de que "registrar" también autentica.
        var authResult = await authService.IssueTokensForUserAsync(user, ct);

        // Una única transacción implícita: Company + User + RefreshToken se confirman juntos o no se confirma nada.
        await db.SaveChangesAsync(ct);

        return new RegisterProviderResponse
        {
            AccessToken = authResult.AccessToken,
            RefreshToken = authResult.RefreshToken,
            ExpiresAtUtc = authResult.ExpiresAtUtc,
            User = authResult.User,
            Company = ToSummary(company)
        };
    }

    public async Task<PagedResult<CompanyResponse>> ListAsync(CompanyStatus? status, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, totalCount) = await companyRepository.ListAsync(status, page, pageSize, ct);

        return new PagedResult<CompanyResponse>
        {
            Items = items.Select(ToResponse).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<CompanyResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var company = await companyRepository.GetByIdAsync(id, ct)
            ?? throw new NotFoundAppException("Empresa no encontrada.");

        return ToResponse(company);
    }

    public async Task<CompanyResponse> ApproveAsync(Guid companyId, Guid adminUserId, CancellationToken ct)
    {
        var company = await companyRepository.GetByIdAsync(companyId, ct)
            ?? throw new NotFoundAppException("Empresa no encontrada.");

        if (company.Status != CompanyStatus.PENDING_APPROVAL)
            throw new ConflictAppException($"Solo se pueden aprobar empresas en estado PENDING_APPROVAL (estado actual: {company.Status}).");

        company.Status = CompanyStatus.APPROVED;
        company.ApprovedByUserId = adminUserId;
        company.ApprovedAt = DateTimeOffset.UtcNow;
        company.RejectionReason = null;

        await db.SaveChangesAsync(ct);

        return ToResponse(company);
    }

    public async Task<CompanyResponse> RejectAsync(Guid companyId, Guid adminUserId, RejectCompanyRequest request, CancellationToken ct)
    {
        var company = await companyRepository.GetByIdAsync(companyId, ct)
            ?? throw new NotFoundAppException("Empresa no encontrada.");

        if (company.Status != CompanyStatus.PENDING_APPROVAL)
            throw new ConflictAppException($"Solo se pueden rechazar empresas en estado PENDING_APPROVAL (estado actual: {company.Status}).");

        company.Status = CompanyStatus.REJECTED;
        company.ApprovedByUserId = adminUserId;
        company.RejectionReason = request.Reason.Trim();
        // ApprovedAt queda nulo a propósito: docs/domain-model.md lo define como "nulo hasta aprobación".

        await db.SaveChangesAsync(ct);

        return ToResponse(company);
    }

    public async Task<CompanyResponse> GetMyCompanyAsync(Guid companyId, CancellationToken ct)
    {
        var company = await companyRepository.GetByIdAsync(companyId, ct)
            ?? throw new NotFoundAppException("Empresa no encontrada.");

        return ToResponse(company);
    }

    public async Task<CompanyResponse> UpdateMyCompanyAsync(Guid companyId, UpdateCompanyRequest request, CancellationToken ct)
    {
        var company = await companyRepository.GetByIdAsync(companyId, ct)
            ?? throw new NotFoundAppException("Empresa no encontrada.");

        if (company.Status != CompanyStatus.APPROVED)
            throw new ConflictAppException("Solo se puede editar el perfil de una empresa aprobada.");

        company.Name = request.Name.Trim();
        company.Description = request.Description?.Trim();
        company.ContactEmail = request.ContactEmail.Trim().ToLowerInvariant();
        company.ContactPhone = request.ContactPhone?.Trim();

        await db.SaveChangesAsync(ct);

        return ToResponse(company);
    }

    private static CompanySummaryResponse ToSummary(Company company) => new()
    {
        Id = company.Id,
        Name = company.Name,
        Status = company.Status.ToString()
    };

    private static CompanyResponse ToResponse(Company company) => new()
    {
        Id = company.Id,
        Name = company.Name,
        Description = company.Description,
        LegalDocument = company.LegalDocument,
        ContactEmail = company.ContactEmail,
        ContactPhone = company.ContactPhone,
        Status = company.Status.ToString(),
        ApprovedAt = company.ApprovedAt,
        RejectionReason = company.RejectionReason,
        CreatedAt = company.CreatedAt
    };
}
