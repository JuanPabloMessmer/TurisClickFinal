using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Categories.Dtos;
using TurisClick.Api.Modules.Categories.Entities;
using TurisClick.Api.Modules.Categories.Repositories;
using TurisClick.Api.Modules.Companies.Entities;
using TurisClick.Api.Modules.Companies.Repositories;
using TurisClick.Api.Modules.Destinations.Entities;
using TurisClick.Api.Modules.Destinations.Repositories;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Experiences.Repositories;
using TurisClick.Api.Shared.Exceptions;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Experiences.Services;

public class ExperienceService(
    IExperienceRepository experienceRepository,
    IDestinationRepository destinationRepository,
    ICategoryRepository categoryRepository,
    ICompanyRepository companyRepository,
    ICurrentUserContext currentUser,
    ICompanyOwnershipGuard ownershipGuard,
    TurisClickDbContext db) : IExperienceService
{
    public async Task<ExperienceResponse> CreateAsync(CreateExperienceRequest request, CancellationToken ct)
    {
        var companyId = RequireCompanyId();

        var company = await companyRepository.GetByIdAsync(companyId, ct)
            ?? throw new NotFoundAppException("Empresa no encontrada.");
        if (company.Status != CompanyStatus.APPROVED)
            throw new ForbiddenAppException("Tu empresa todavía no está aprobada; no podés crear experiencias.");

        await EnsureValidDestinationAsync(request.DestinationId, ct);
        var categories = await ValidateAndLoadCategoriesAsync(request.CategoryIds, ct);
        EnsureValidImages(request.Images);

        var experience = new Experience
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            DestinationId = request.DestinationId,
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            IncludesText = NullIfBlank(request.IncludesText),
            ExcludesText = NullIfBlank(request.ExcludesText),
            DurationMinutes = request.DurationMinutes,
            DurationLabel = NullIfBlank(request.DurationLabel),
            Price = request.Price,
            Currency = request.Currency,
            Status = PublicationStatus.DRAFT,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        foreach (var category in categories)
            experience.Categories.Add(category);
        foreach (var image in BuildImages(request.Images))
            experience.Images.Add(image);

        await experienceRepository.AddAsync(experience, ct);
        await db.SaveChangesAsync(ct);

        return await GetOwnedByIdAsync(experience.Id, ct);
    }

    public async Task<ExperienceResponse> UpdateAsync(Guid experienceId, UpdateExperienceRequest request, CancellationToken ct)
    {
        var experience = await experienceRepository.GetByIdForUpdateAsync(experienceId, ct)
            ?? throw new NotFoundAppException("Experiencia no encontrada.");

        ownershipGuard.EnsureOwns(experience.CompanyId);

        await EnsureValidDestinationAsync(request.DestinationId, ct);
        var categories = await ValidateAndLoadCategoriesAsync(request.CategoryIds, ct);
        EnsureValidImages(request.Images);

        experience.DestinationId = request.DestinationId;
        experience.Title = request.Title.Trim();
        experience.Description = request.Description.Trim();
        experience.IncludesText = NullIfBlank(request.IncludesText);
        experience.ExcludesText = NullIfBlank(request.ExcludesText);
        experience.DurationMinutes = request.DurationMinutes;
        experience.DurationLabel = NullIfBlank(request.DurationLabel);
        experience.Price = request.Price;
        experience.Currency = request.Currency;
        experience.UpdatedAt = DateTimeOffset.UtcNow;

        experience.Categories.Clear();
        foreach (var category in categories)
            experience.Categories.Add(category);

        experience.Images.Clear();
        foreach (var image in BuildImages(request.Images))
            experience.Images.Add(image);

        await db.SaveChangesAsync(ct);

        return await GetOwnedByIdAsync(experienceId, ct);
    }

    public async Task<ExperienceResponse> PublishAsync(Guid experienceId, CancellationToken ct)
    {
        var experience = await experienceRepository.GetByIdForUpdateAsync(experienceId, ct)
            ?? throw new NotFoundAppException("Experiencia no encontrada.");

        ownershipGuard.EnsureOwns(experience.CompanyId);

        if (experience.Status != PublicationStatus.PUBLISHED)
        {
            // Regla explícita (no basta con cambiar el estado): ver docs/use-cases.md UC-P-06.
            if (!await experienceRepository.HasFutureOpenAvailabilityAsync(experienceId, ct))
                throw new ConflictAppException(
                    "La experiencia necesita al menos una disponibilidad futura con cupo (OPEN) antes de publicarse.");

            experience.Status = PublicationStatus.PUBLISHED;
            experience.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return await GetOwnedByIdAsync(experienceId, ct);
    }

    public async Task<ExperienceResponse> UnpublishAsync(Guid experienceId, CancellationToken ct)
    {
        var experience = await experienceRepository.GetByIdForUpdateAsync(experienceId, ct)
            ?? throw new NotFoundAppException("Experiencia no encontrada.");

        ownershipGuard.EnsureOwns(experience.CompanyId);

        if (experience.Status != PublicationStatus.UNPUBLISHED)
        {
            experience.Status = PublicationStatus.UNPUBLISHED;
            experience.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return await GetOwnedByIdAsync(experienceId, ct);
    }

    public async Task<ExperienceResponse> GetOwnedByIdAsync(Guid experienceId, CancellationToken ct)
    {
        var experience = await experienceRepository.GetByIdForReadAsync(experienceId, ct)
            ?? throw new NotFoundAppException("Experiencia no encontrada.");

        ownershipGuard.EnsureOwns(experience.CompanyId);

        return ToResponse(experience);
    }

    public async Task<PagedResult<ExperienceSummaryResponse>> ListOwnedAsync(int page, int pageSize, CancellationToken ct)
    {
        var companyId = RequireCompanyId();
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, totalCount) = await experienceRepository.ListByCompanyAsync(companyId, page, pageSize, ct);

        return new PagedResult<ExperienceSummaryResponse>
        {
            Items = items.Select(ToSummary).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<ExperienceResponse> GetPublishedByIdAsync(Guid id, CancellationToken ct)
    {
        var experience = await experienceRepository.GetByIdForReadAsync(id, ct);

        if (experience is null || experience.Status != PublicationStatus.PUBLISHED)
            throw new NotFoundAppException("Experiencia no encontrada.");

        return ToResponse(experience);
    }

    public async Task<PagedResult<ExperienceSummaryResponse>> SearchAsync(ExperienceSearchFilter filter, CancellationToken ct)
    {
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var normalizedFilter = filter with { Page = page, PageSize = pageSize };

        var (items, totalCount) = await experienceRepository.SearchAsync(normalizedFilter, ct);

        return new PagedResult<ExperienceSummaryResponse>
        {
            Items = items.Select(ToSummary).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    private Guid RequireCompanyId() =>
        currentUser.CompanyId ?? throw new ForbiddenAppException("El usuario autenticado no tiene una empresa asociada.");

    /// <summary>docs/domain-model.md regla 10: Experience.DestinationId siempre apunta a un destino de tipo CITY.</summary>
    private async Task EnsureValidDestinationAsync(Guid destinationId, CancellationToken ct)
    {
        var destination = await destinationRepository.GetByIdAsync(destinationId, ct)
            ?? throw new ValidationAppException("El destino indicado no existe.");

        if (destination.Type != DestinationType.CITY)
            throw new ValidationAppException("El destino de una experiencia debe ser una ciudad (CITY).");
    }

    private async Task<List<Category>> ValidateAndLoadCategoriesAsync(List<Guid> categoryIds, CancellationToken ct)
    {
        if (categoryIds.Count == 0)
            return [];

        var distinctIds = categoryIds.Distinct().ToList();
        var categories = await categoryRepository.GetByIdsAsync(distinctIds, ct);

        if (categories.Count != distinctIds.Count)
            throw new ValidationAppException("Una o más categorías indicadas no existen.");

        return categories;
    }

    private static void EnsureValidImages(List<ExperienceImageRequest> images)
    {
        if (images.Count(i => i.IsCover) > 1)
            throw new ValidationAppException("Solo una imagen puede marcarse como portada (IsCover).");
    }

    /// <summary>Si no se marcó ninguna portada explícitamente, la primera imagen queda como portada por defecto.</summary>
    private static List<ExperienceImage> BuildImages(List<ExperienceImageRequest> images)
    {
        if (images.Count == 0)
            return [];

        var hasExplicitCover = images.Any(i => i.IsCover);

        return images.Select((image, index) => new ExperienceImage
        {
            Id = Guid.NewGuid(),
            Url = image.Url,
            SortOrder = index,
            IsCover = hasExplicitCover ? image.IsCover : index == 0
        }).ToList();
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ExperienceResponse ToResponse(Experience experience) => new()
    {
        Id = experience.Id,
        CompanyId = experience.CompanyId,
        CompanyName = experience.Company!.Name,
        DestinationId = experience.DestinationId,
        DestinationName = experience.Destination!.Name,
        Title = experience.Title,
        Description = experience.Description,
        IncludesText = experience.IncludesText,
        ExcludesText = experience.ExcludesText,
        DurationMinutes = experience.DurationMinutes,
        DurationLabel = experience.DurationLabel,
        Price = experience.Price,
        Currency = experience.Currency,
        Status = experience.Status.ToString(),
        Categories = experience.Categories
            .Select(c => new CategoryResponse { Id = c.Id, Name = c.Name, Description = c.Description })
            .ToList(),
        Images = experience.Images
            .OrderBy(i => i.SortOrder)
            .Select(i => new ExperienceImageResponse { Id = i.Id, Url = i.Url, SortOrder = i.SortOrder, IsCover = i.IsCover })
            .ToList(),
        CreatedAt = experience.CreatedAt,
        UpdatedAt = experience.UpdatedAt
    };

    private static ExperienceSummaryResponse ToSummary(Experience experience) => new()
    {
        Id = experience.Id,
        Title = experience.Title,
        DestinationName = experience.Destination!.Name,
        Price = experience.Price,
        Currency = experience.Currency,
        DurationLabel = experience.DurationLabel,
        CoverImageUrl = experience.Images.FirstOrDefault(i => i.IsCover)?.Url,
        CompanyName = experience.Company!.Name,
        Status = experience.Status.ToString()
    };
}
