using Microsoft.EntityFrameworkCore;
using Npgsql;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Companies.Dtos;
using TurisClick.Api.Modules.Companies.Entities;
using TurisClick.Api.Modules.Companies.Repositories;
using TurisClick.Api.Shared.Exceptions;
using TurisClick.Api.Shared.Responses;
using TurisClick.Api.Shared.Validation;

namespace TurisClick.Api.Modules.Companies.Services;

public class CompanyService(ICompanyRepository companyRepository, TurisClickDbContext db) : ICompanyService
{
    // El autorregistro público de operadores se eliminó en la Oleada 13: una empresa de turismo no se da de
    // alta sola en TurisClick, y un endpoint anónimo capaz de crear una cuenta con rol PROVIDER era además una
    // escalada de privilegios. El alta la hace un administrador — ver AdminOnboardingService.

    public async Task<PagedResult<CompanyResponse>> ListAsync(CompanyStatus? status, string? search, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, totalCount) = await companyRepository.ListAsync(status, search, page, pageSize, ct);

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
            throw new ConflictAppException($"Solo se pueden aprobar empresas pendientes de aprobación (esta empresa está {StatusLabel(company.Status)}).");

        company.Status = CompanyStatus.APPROVED;
        company.ApprovedByUserId = adminUserId;
        company.ApprovedAt = DateTimeOffset.UtcNow;
        company.RejectionReason = null;

        await db.SaveChangesAsync(ct);

        return ToResponse(company);
    }

    /// <summary>
    /// UC-A-08 — suspender una empresa incumplidora. No hay cascada sobre sus productos: la suspensión
    /// actúa como filtro de visibilidad y de operación (catálogo público, retrieval de la IA, reservas y
    /// publicación), así que al reactivarla cada Experience/Package vuelve con el estado que ya tenía.
    /// </summary>
    public async Task<CompanyResponse> SuspendAsync(Guid companyId, CancellationToken ct)
    {
        var company = await companyRepository.GetByIdAsync(companyId, ct)
            ?? throw new NotFoundAppException("Empresa no encontrada.");

        if (company.Status == CompanyStatus.SUSPENDED)
            return ToResponse(company);

        if (company.Status != CompanyStatus.APPROVED)
            throw new ConflictAppException($"Solo se puede suspender una empresa aprobada (esta empresa está {StatusLabel(company.Status)}).");

        company.Status = CompanyStatus.SUSPENDED;
        await db.SaveChangesAsync(ct);

        return ToResponse(company);
    }

    /// <summary>UC-A-08 — levantar la suspensión. Devuelve la empresa a APPROVED sin tocar el estado de sus productos.</summary>
    public async Task<CompanyResponse> ReactivateAsync(Guid companyId, CancellationToken ct)
    {
        var company = await companyRepository.GetByIdAsync(companyId, ct)
            ?? throw new NotFoundAppException("Empresa no encontrada.");

        if (company.Status != CompanyStatus.SUSPENDED)
            throw new ConflictAppException($"Solo se puede reactivar una empresa suspendida (esta empresa está {StatusLabel(company.Status)}).");

        company.Status = CompanyStatus.APPROVED;
        await db.SaveChangesAsync(ct);

        return ToResponse(company);
    }

    public async Task<CompanyResponse> RejectAsync(Guid companyId, Guid adminUserId, RejectCompanyRequest request, CancellationToken ct)
    {
        var company = await companyRepository.GetByIdAsync(companyId, ct)
            ?? throw new NotFoundAppException("Empresa no encontrada.");

        if (company.Status != CompanyStatus.PENDING_APPROVAL)
            throw new ConflictAppException($"Solo se pueden rechazar empresas pendientes de aprobación (esta empresa está {StatusLabel(company.Status)}).");

        company.Status = CompanyStatus.REJECTED;
        company.ApprovedByUserId = adminUserId;
        company.RejectionReason = TextNormalizer.MultiLine(request.Reason);
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

        // Mismos valores normalizados que validaron los atributos del DTO (R-01, R-02, R-04, R-05).
        var name = TextNormalizer.SingleLine(request.Name) ?? string.Empty;
        var conflict = await companyRepository.FindNameConflictAsync(TextNormalizer.NameKey(name), companyId, ct);
        if (conflict is not null)
            throw DuplicateName(conflict.Name);

        company.Name = name;
        company.Description = TextNormalizer.NullIfEmpty(TextNormalizer.MultiLine(request.Description));
        company.ContactEmail = FieldRules.Email.Normalize(request.ContactEmail) ?? string.Empty;
        company.ContactPhone = FieldRules.Phone.Parse(request.ContactPhone).Value;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresUniqueViolation })
        {
            // Otra empresa tomó el mismo nombre entre la verificación y el guardado: el índice único decide.
            throw DuplicateName(name);
        }

        return ToResponse(company);
    }

    public async Task<CompanyNameCheckResponse> CheckNameAsync(string? name, Guid? excludeId, CancellationToken ct)
    {
        var normalized = TextNormalizer.SingleLine(name) ?? string.Empty;
        var response = new CompanyNameCheckResponse { Name = normalized };
        if (normalized.Length == 0) return response;

        var exact = await companyRepository.FindNameConflictAsync(TextNormalizer.NameKey(normalized), excludeId, ct);
        if (exact is not null)
            response.ExactMatch = new CompanyNameMatchResponse { Id = exact.Id, Name = exact.Name };

        response.Similar = (await companyRepository.FindSimilarAsync(normalized, excludeId, ct))
            .Select(m => new CompanyNameMatchResponse { Id = m.Id, Name = m.Name })
            .ToList();
        return response;
    }

    private const string PostgresUniqueViolation = "23505";

    /// <summary>"Ya existe…" para el campo del nombre (en Mi empresa el campo se llama <c>name</c>).</summary>
    public static ConflictAppException DuplicateName(string existing, string field = "name") =>
        new($"Ya existe una empresa con ese nombre: «{existing}».", ErrorCodes.Duplicate, field);

    private static string StatusLabel(CompanyStatus status) => status switch
    {
        CompanyStatus.PENDING_APPROVAL => "pendiente de aprobación",
        CompanyStatus.APPROVED => "aprobada",
        CompanyStatus.REJECTED => "rechazada",
        CompanyStatus.SUSPENDED => "suspendida",
        _ => status.ToString().ToLowerInvariant(),
    };

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
        CreatedAt = company.CreatedAt,
        CanSellFlights = company.CanSellFlights,
        FlightSellerRegistration = company.FlightSellerRegistration,
        FlightSellerAuthorizedAt = company.FlightSellerAuthorizedAt
    };
}
