using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Categories.Dtos;
using TurisClick.Api.Modules.Preferences.Dtos;
using TurisClick.Api.Modules.Preferences.Entities;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Preferences.Services;

public interface ITouristPreferenceService
{
    /// <summary>Del turista autenticado. Si nunca guardó nada, devuelve un perfil vacío (no 404).</summary>
    Task<TouristPreferencesResponse> GetMineAsync(CancellationToken ct);

    Task<TouristPreferencesResponse> UpdateMineAsync(UpdateTouristPreferencesRequest request, CancellationToken ct);

    /// <summary>Lectura para otros módulos (asistente IA). Null si el turista no tiene perfil.</summary>
    Task<TouristPreference?> FindForUserAsync(Guid userId, CancellationToken ct);
}

public class TouristPreferenceService(TurisClickDbContext db, ICurrentUserContext currentUser) : ITouristPreferenceService
{
    /// <summary>Techo razonable de intereses: el catálogo tiene pocas categorías y más no aporta señal.</summary>
    private const int MaxCategories = 12;

    public async Task<TouristPreferencesResponse> GetMineAsync(CancellationToken ct) =>
        ToResponse(await FindForUserAsync(currentUser.UserId, ct));

    public Task<TouristPreference?> FindForUserAsync(Guid userId, CancellationToken ct) =>
        db.Set<TouristPreference>()
            .AsNoTracking()
            .Include(p => p.Categories)
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

    public async Task<TouristPreferencesResponse> UpdateMineAsync(UpdateTouristPreferencesRequest request, CancellationToken ct)
    {
        var pace = ParseOptional<TravelPace>(request.TravelPace, "ritmo de viaje");
        var party = ParseOptional<TravelParty>(request.TravelParty, "con quién viajás");
        var budget = ParseOptional<BudgetLevel>(request.BudgetLevel, "presupuesto");

        var categoryIds = request.CategoryIds.Distinct().ToList();
        if (categoryIds.Count > MaxCategories)
            throw new ValidationAppException($"Elegí hasta {MaxCategories} intereses.");

        var categories = await db.Categories.Where(c => categoryIds.Contains(c.Id)).ToListAsync(ct);
        if (categories.Count != categoryIds.Count)
            throw new ValidationAppException("Alguno de los intereses elegidos no existe.");

        var now = DateTimeOffset.UtcNow;
        var preference = await db.Set<TouristPreference>()
            .Include(p => p.Categories)
            .FirstOrDefaultAsync(p => p.UserId == currentUser.UserId, ct);

        if (preference is null)
        {
            preference = new TouristPreference { UserId = currentUser.UserId, CreatedAt = now };
            db.Add(preference);
        }

        preference.TravelPace = pace;
        preference.TravelParty = party;
        preference.BudgetLevel = budget;
        preference.UpdatedAt = now;
        if (request.CompleteOnboarding && preference.OnboardingCompletedAt is null)
            preference.OnboardingCompletedAt = now;

        preference.Categories.Clear();
        foreach (var category in categories)
            preference.Categories.Add(category);

        await db.SaveChangesAsync(ct);

        return await GetMineAsync(ct);
    }

    private static TEnum? ParseOptional<TEnum>(string? value, string field) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ValidationAppException($"Valor inválido para {field}: {value}. Opciones: {string.Join(", ", Enum.GetNames<TEnum>())}.");
    }

    private static TouristPreferencesResponse ToResponse(TouristPreference? preference) => preference is null
        ? new TouristPreferencesResponse()
        : new TouristPreferencesResponse
        {
            Categories = preference.Categories
                .OrderBy(c => c.Name)
                .Select(c => new CategoryResponse { Id = c.Id, Name = c.Name, Description = c.Description })
                .ToList(),
            TravelPace = preference.TravelPace?.ToString(),
            TravelParty = preference.TravelParty?.ToString(),
            BudgetLevel = preference.BudgetLevel?.ToString(),
            OnboardingCompleted = preference.OnboardingCompletedAt is not null,
            OnboardingCompletedAt = preference.OnboardingCompletedAt,
            UpdatedAt = preference.UpdatedAt
        };
}
