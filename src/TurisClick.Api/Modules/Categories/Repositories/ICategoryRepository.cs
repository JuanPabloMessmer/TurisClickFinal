using TurisClick.Api.Modules.Categories.Entities;

namespace TurisClick.Api.Modules.Categories.Repositories;

/// <summary>Dónde se usa una categoría: si alguno es mayor que cero, no se puede eliminar (R-09).</summary>
public sealed record CategoryUsage(int Experiences, int Packages, int Tourists)
{
    public bool InUse => Experiences > 0 || Packages > 0 || Tourists > 0;
}

public interface ICategoryRepository
{
    Task<Category?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<List<Category>> ListAsync(CancellationToken ct);

    /// <summary>Usado por otros módulos (ej. Experiences) para validar referencias antes de asociarlas.</summary>
    Task<List<Category>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>
    /// Nombre de la categoría (distinta de <paramref name="excludeId"/>) cuya clave normalizada K1 es
    /// <paramref name="nameKey"/>, o null si no hay ninguna. Usa la misma expresión que el índice único.
    /// </summary>
    Task<string?> FindNameConflictAsync(string nameKey, Guid? excludeId, CancellationToken ct);

    Task<CategoryUsage> GetUsageAsync(Guid id, CancellationToken ct);
    Task AddAsync(Category category, CancellationToken ct);
    void Remove(Category category);
}
