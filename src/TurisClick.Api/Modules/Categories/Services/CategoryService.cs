using Microsoft.EntityFrameworkCore;
using Npgsql;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Categories.Dtos;
using TurisClick.Api.Modules.Categories.Entities;
using TurisClick.Api.Modules.Categories.Repositories;
using TurisClick.Api.Shared.Exceptions;
using TurisClick.Api.Shared.Validation;

namespace TurisClick.Api.Modules.Categories.Services;

/// <summary>
/// UC-A-05 — Gestionar categorías. Reglas R-07 a R-09 de docs/auditoria-validaciones.md: nombre normalizado,
/// unicidad sin distinguir mayúsculas ni tildes (también en la base, por índice) y borrado bloqueado si la
/// categoría está en uso.
/// </summary>
public class CategoryService(ICategoryRepository categoryRepository, TurisClickDbContext db) : ICategoryService
{
    private const string PostgresUniqueViolation = "23505";
    private const string PostgresForeignKeyViolation = "23503";

    public async Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, CancellationToken ct)
    {
        var (name, description) = Normalize(request.Name, request.Description);
        await EnsureNameIsFreeAsync(name, excludeId: null, ct);

        var category = new Category { Id = Guid.NewGuid(), Name = name, Description = description };

        await categoryRepository.AddAsync(category, ct);
        await SaveAsync(name, ct);

        return ToResponse(category);
    }

    public async Task<CategoryResponse> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken ct)
    {
        var category = await categoryRepository.GetByIdAsync(id, ct)
            ?? throw new NotFoundAppException("Categoría no encontrada.");

        var (name, description) = Normalize(request.Name, request.Description);
        await EnsureNameIsFreeAsync(name, excludeId: id, ct);

        category.Name = name;
        category.Description = description;
        await SaveAsync(name, ct);

        return ToResponse(category);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var category = await categoryRepository.GetByIdAsync(id, ct)
            ?? throw new NotFoundAppException("Categoría no encontrada.");

        var usage = await categoryRepository.GetUsageAsync(id, ct);
        if (usage.InUse)
            throw new ConflictAppException(InUseMessage(category.Name, usage), ErrorCodes.CategoryInUse);

        categoryRepository.Remove(category);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresForeignKeyViolation })
        {
            // Alguien la asignó entre la verificación y el borrado: la FK con RESTRICT la protege.
            throw new ConflictAppException(
                $"No se puede eliminar «{category.Name}» porque acaba de asignarse a una experiencia, un paquete o las preferencias de un turista.",
                ErrorCodes.CategoryInUse);
        }
    }

    public async Task<CategoryResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var category = await categoryRepository.GetByIdAsync(id, ct)
            ?? throw new NotFoundAppException("Categoría no encontrada.");

        return ToResponse(category);
    }

    public async Task<List<CategoryResponse>> ListAsync(CancellationToken ct)
    {
        var categories = await categoryRepository.ListAsync(ct);
        return categories.Select(ToResponse).ToList();
    }

    /// <summary>Mismo texto que validó el atributo del DTO: nombre de una línea y descripción opcional.</summary>
    private static (string Name, string? Description) Normalize(string name, string? description) =>
        (TextNormalizer.SingleLine(name) ?? string.Empty,
            TextNormalizer.NullIfEmpty(TextNormalizer.SingleLine(description)));

    private async Task EnsureNameIsFreeAsync(string name, Guid? excludeId, CancellationToken ct)
    {
        var existing = await categoryRepository.FindNameConflictAsync(TextNormalizer.NameKey(name), excludeId, ct);
        if (existing is not null)
            throw DuplicateName(existing);
    }

    private async Task SaveAsync(string name, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresUniqueViolation })
        {
            // Dos administradores guardando el mismo nombre a la vez: el índice único decide.
            throw DuplicateName(name);
        }
    }

    private static ConflictAppException DuplicateName(string existing) =>
        new($"Ya existe la categoría «{existing}».", ErrorCodes.Duplicate, field: "name");

    public static string InUseMessage(string name, CategoryUsage usage)
    {
        var parts = new List<string>();
        if (usage.Experiences > 0) parts.Add(Plural(usage.Experiences, "experiencia", "experiencias"));
        if (usage.Packages > 0) parts.Add(Plural(usage.Packages, "paquete", "paquetes"));

        var assigned = parts.Count > 0 ? $"está asignada a {string.Join(" y ", parts)}" : null;
        var tourists = usage.Tourists > 0
            ? $"{Plural(usage.Tourists, "turista la eligió", "turistas la eligieron")} como interés"
            : null;
        var reason = (assigned, tourists) switch
        {
            ({ } a, { } t) => $"{a}, y {t}",
            ({ } a, null) => a,
            (null, { } t) => t,
            _ => "está en uso",
        };
        return $"No se puede eliminar «{name}» porque {reason}. Quitala de esos elementos antes de eliminarla.";
    }

    private static string Plural(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";

    private static CategoryResponse ToResponse(Category category) => new()
    {
        Id = category.Id,
        Name = category.Name,
        Description = category.Description
    };
}
