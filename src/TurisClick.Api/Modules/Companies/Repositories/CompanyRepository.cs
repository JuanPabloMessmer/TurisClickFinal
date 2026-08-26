using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Companies.Entities;

namespace TurisClick.Api.Modules.Companies.Repositories;

public class CompanyRepository(TurisClickDbContext db) : ICompanyRepository
{
    public Task<Company?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<bool> LegalDocumentExistsAsync(string legalDocument, CancellationToken ct) =>
        db.Companies.AnyAsync(c => c.LegalDocument == legalDocument, ct);

    public async Task<(List<Company> Items, int TotalCount)> ListAsync(
        CompanyStatus? status, string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Companies.AsQueryable();

        if (status.HasValue)
            query = query.Where(c => c.Status == status);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // ILIKE (Postgres, case-insensitive) — patrón simple "%term%", suficiente para el volumen
            // de empresas que maneja el panel de un ADMIN; no hace falta full-text search todavía.
            var pattern = $"%{search.Trim()}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.Name, pattern) ||
                EF.Functions.ILike(c.LegalDocument, pattern) ||
                EF.Functions.ILike(c.ContactEmail, pattern));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task AddAsync(Company company, CancellationToken ct) =>
        await db.Companies.AddAsync(company, ct);
}
