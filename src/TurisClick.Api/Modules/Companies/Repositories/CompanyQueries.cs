using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Shared.Validation;

namespace TurisClick.Api.Modules.Companies.Repositories;

/// <summary>Una empresa existente cuyo nombre choca o se parece al que se quiere usar.</summary>
public sealed class CompanyNameMatch
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Consultas de unicidad de empresas (R-01 y R-03). Usan las mismas expresiones que los índices únicos de la
/// migración 0016 (<see cref="NameKeySql"/>), así la verificación previa del servicio y la base coinciden.
/// Las expresiones son constantes del código; los valores del usuario van siempre como parámetros.
/// </summary>
public static class CompanyQueries
{
    // SQL fijo armado con constantes del código (NameKeySql); los valores del usuario van como {0}/{1}.
    private static readonly string NameConflictSql =
        $"SELECT id AS \"Id\", name AS \"Name\" FROM companies WHERE {NameKeySql.NameKey("name")} = {{0}} AND id <> {{1}} LIMIT 1";

    private static readonly string NitConflictSql =
        $"SELECT id AS \"Id\", name AS \"Name\" FROM companies WHERE {NameKeySql.NormalizedNit("legal_document")} = {{0}} LIMIT 1";

    public static Task<CompanyNameMatch?> FindNameConflictAsync(
        TurisClickDbContext db, string nameKey, Guid? excludeId, CancellationToken ct) =>
        db.Database
            .SqlQueryRaw<CompanyNameMatch>(NameConflictSql, nameKey, excludeId ?? Guid.Empty)
            .FirstOrDefaultAsync(ct);

    /// <summary>Empresa con el mismo NIT normalizado (sin espacios, puntos ni guiones), si existe.</summary>
    public static Task<CompanyNameMatch?> FindNitConflictAsync(TurisClickDbContext db, string normalizedNit, CancellationToken ct) =>
        db.Database
            .SqlQueryRaw<CompanyNameMatch>(NitConflictSql, normalizedNit)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Empresas con la misma clave de similitud K2 (puntuación y forma societaria aparte) pero distinta K1.
    /// Se calcula en memoria: el volumen de empresas del panel es de decenas, no de miles.
    /// </summary>
    public static async Task<List<CompanyNameMatch>> FindSimilarAsync(
        TurisClickDbContext db, string name, Guid? excludeId, CancellationToken ct)
    {
        var key = TextNormalizer.NameKey(name);
        var similarity = TextNormalizer.SimilarityKey(name);
        if (similarity.Length == 0) return [];

        var all = await db.Companies.AsNoTracking()
            .Where(c => excludeId == null || c.Id != excludeId)
            .Select(c => new CompanyNameMatch { Id = c.Id, Name = c.Name })
            .ToListAsync(ct);

        return all
            .Where(c => TextNormalizer.SimilarityKey(c.Name) == similarity && TextNormalizer.NameKey(c.Name) != key)
            .OrderBy(c => c.Name, StringComparer.CurrentCulture)
            .Take(10)
            .ToList();
    }
}
