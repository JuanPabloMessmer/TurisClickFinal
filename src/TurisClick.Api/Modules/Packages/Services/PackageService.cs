using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Categories.Dtos;
using TurisClick.Api.Modules.Categories.Entities;
using TurisClick.Api.Modules.Categories.Repositories;
using TurisClick.Api.Modules.Companies.Entities;
using TurisClick.Api.Modules.Companies.Repositories;
using TurisClick.Api.Modules.Destinations.Entities;
using TurisClick.Api.Modules.Destinations.Repositories;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Experiences.Repositories;
using TurisClick.Api.Modules.Packages.Dtos;
using TurisClick.Api.Modules.Packages.Entities;
using TurisClick.Api.Modules.Packages.Repositories;
using TurisClick.Api.Shared.Exceptions;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Packages.Services;

public class PackageService(
    IPackageRepository packageRepository,
    IDestinationRepository destinationRepository,
    ICategoryRepository categoryRepository,
    ICompanyRepository companyRepository,
    IExperienceRepository experienceRepository,
    ICurrentUserContext currentUser,
    ICompanyOwnershipGuard ownershipGuard,
    TurisClickDbContext db) : IPackageService
{
    public async Task<PackageResponse> CreateAsync(CreatePackageRequest request, CancellationToken ct)
    {
        var companyId = RequireCompanyId();

        var company = await companyRepository.GetByIdAsync(companyId, ct)
            ?? throw new NotFoundAppException("Empresa no encontrada.");
        if (company.Status != CompanyStatus.APPROVED)
            throw new ForbiddenAppException("Tu empresa todavía no está aprobada; no podés crear paquetes.");

        await EnsureValidDestinationAsync(request.DestinationId, ct);
        var categories = await ValidateAndLoadCategoriesAsync(request.CategoryIds, ct);
        EnsureValidImages(request.Images);
        var items = await BuildAndValidateItemsAsync(request.Items, companyId, ct);

        var package = new Package
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            DestinationId = request.DestinationId,
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            ConditionsText = NullIfBlank(request.ConditionsText),
            DurationDays = request.DurationDays,
            Price = request.Price,
            Currency = request.Currency,
            Status = PublicationStatus.DRAFT,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        foreach (var category in categories)
            package.Categories.Add(category);
        foreach (var image in BuildImages(request.Images))
            package.Images.Add(image);
        foreach (var item in items)
            package.Items.Add(item);

        await packageRepository.AddAsync(package, ct);
        await db.SaveChangesAsync(ct);

        return await GetOwnedByIdAsync(package.Id, ct);
    }

    public async Task<PackageResponse> UpdateAsync(Guid packageId, UpdatePackageRequest request, CancellationToken ct)
    {
        var package = await packageRepository.GetByIdForUpdateAsync(packageId, ct)
            ?? throw new NotFoundAppException("Paquete no encontrado.");

        ownershipGuard.EnsureOwns(package.CompanyId);

        await EnsureValidDestinationAsync(request.DestinationId, ct);
        var categories = await ValidateAndLoadCategoriesAsync(request.CategoryIds, ct);
        EnsureValidImages(request.Images);
        var items = await BuildAndValidateItemsAsync(request.Items, package.CompanyId, ct);

        package.DestinationId = request.DestinationId;
        package.Title = request.Title.Trim();
        package.Description = request.Description.Trim();
        package.ConditionsText = NullIfBlank(request.ConditionsText);
        package.DurationDays = request.DurationDays;
        package.Price = request.Price;
        package.Currency = request.Currency;
        package.UpdatedAt = DateTimeOffset.UtcNow;

        package.Categories.Clear();
        foreach (var category in categories)
            package.Categories.Add(category);

        // Items/Images: reemplazo explícito vía el DbSet, NO Clear()+Add() sobre la navegación.
        // Motivo (bug de EF Core encontrado durante Oleada 4): cuando una colección 1-a-N ya trackeada
        // se vacía y se le agregan ítems nuevos en la misma pasada de DetectChanges, EF Core puede
        // emparejar el ítem "borrado" con el "nuevo" que ocupa la misma posición y tratarlo como un
        // UPDATE (Modified) en vez de DELETE+INSERT — el UPDATE no encuentra la fila (Id distinto) y
        // SaveChanges lanza DbUpdateConcurrencyException ("0 filas afectadas"). Pasar por el DbSet
        // evita la ambigüedad: cada ítem viejo se marca Deleted explícitamente y cada nuevo, Added.
        db.PackageImages.RemoveRange(package.Images);
        foreach (var image in BuildImages(request.Images))
        {
            image.PackageId = package.Id;
            db.PackageImages.Add(image);
        }

        db.PackageItems.RemoveRange(package.Items);
        foreach (var item in items)
        {
            item.PackageId = package.Id;
            db.PackageItems.Add(item);
        }

        await db.SaveChangesAsync(ct);

        return await GetOwnedByIdAsync(packageId, ct);
    }

    public async Task<PackageResponse> PublishAsync(Guid packageId, CancellationToken ct)
    {
        var package = await packageRepository.GetByIdForUpdateAsync(packageId, ct)
            ?? throw new NotFoundAppException("Paquete no encontrado.");

        ownershipGuard.EnsureOwns(package.CompanyId);

        if (package.Status != PublicationStatus.PUBLISHED)
        {
            // Reglas explícitas (no basta con cambiar el estado): ver docs/use-cases.md UC-P-09.
            if (package.Items.Count == 0)
                throw new ConflictAppException("El paquete necesita al menos un ítem (Experience o descriptivo) antes de publicarse.");

            if (!await packageRepository.HasFutureOpenAvailabilityAsync(packageId, ct))
                throw new ConflictAppException(
                    "El paquete necesita al menos una disponibilidad futura con cupo (OPEN) antes de publicarse.");

            package.Status = PublicationStatus.PUBLISHED;
            package.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return await GetOwnedByIdAsync(packageId, ct);
    }

    public async Task<PackageResponse> UnpublishAsync(Guid packageId, CancellationToken ct)
    {
        var package = await packageRepository.GetByIdForUpdateAsync(packageId, ct)
            ?? throw new NotFoundAppException("Paquete no encontrado.");

        ownershipGuard.EnsureOwns(package.CompanyId);

        if (package.Status != PublicationStatus.UNPUBLISHED)
        {
            package.Status = PublicationStatus.UNPUBLISHED;
            package.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return await GetOwnedByIdAsync(packageId, ct);
    }

    public async Task<PackageResponse> GetOwnedByIdAsync(Guid packageId, CancellationToken ct)
    {
        var package = await packageRepository.GetByIdForReadAsync(packageId, ct)
            ?? throw new NotFoundAppException("Paquete no encontrado.");

        ownershipGuard.EnsureOwns(package.CompanyId);

        return ToResponse(package);
    }

    public async Task<PagedResult<PackageSummaryResponse>> ListOwnedAsync(int page, int pageSize, CancellationToken ct)
    {
        var companyId = RequireCompanyId();
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, totalCount) = await packageRepository.ListByCompanyAsync(companyId, page, pageSize, ct);

        return new PagedResult<PackageSummaryResponse>
        {
            Items = items.Select(ToSummary).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<PackageResponse> GetPublishedByIdAsync(Guid id, CancellationToken ct)
    {
        var package = await packageRepository.GetByIdForReadAsync(id, ct);

        if (package is null || package.Status != PublicationStatus.PUBLISHED)
            throw new NotFoundAppException("Paquete no encontrado.");

        return ToResponse(package);
    }

    public async Task<PagedResult<PackageSummaryResponse>> SearchAsync(PackageSearchFilter filter, CancellationToken ct)
    {
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var normalizedFilter = filter with { Page = page, PageSize = pageSize };

        var (items, totalCount) = await packageRepository.SearchAsync(normalizedFilter, ct);

        return new PagedResult<PackageSummaryResponse>
        {
            Items = items.Select(ToSummary).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    private Guid RequireCompanyId() =>
        currentUser.CompanyId ?? throw new ForbiddenAppException("El usuario autenticado no tiene una empresa asociada.");

    /// <summary>docs/domain-model.md regla 10: Package.DestinationId (destino principal) siempre apunta a un destino de tipo CITY.</summary>
    private async Task EnsureValidDestinationAsync(Guid destinationId, CancellationToken ct)
    {
        var destination = await destinationRepository.GetByIdAsync(destinationId, ct)
            ?? throw new ValidationAppException("El destino indicado no existe.");

        if (destination.Type != DestinationType.CITY)
            throw new ValidationAppException("El destino de un paquete debe ser una ciudad (CITY).");
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

    private static void EnsureValidImages(List<PackageImageRequest> images)
    {
        if (images.Count(i => i.IsCover) > 1)
            throw new ValidationAppException("Solo una imagen puede marcarse como portada (IsCover).");
    }

    /// <summary>Si no se marcó ninguna portada explícitamente, la primera imagen queda como portada por defecto.</summary>
    private static List<PackageImage> BuildImages(List<PackageImageRequest> images)
    {
        if (images.Count == 0)
            return [];

        var hasExplicitCover = images.Any(i => i.IsCover);

        return images.Select((image, index) => new PackageImage
        {
            Id = Guid.NewGuid(),
            Url = image.Url,
            SortOrder = index,
            IsCover = hasExplicitCover ? image.IsCover : index == 0
        }).ToList();
    }

    /// <summary>
    /// docs/domain-model.md §5, regla 3: un PackageItem EXPERIENCE_REFERENCE solo puede referenciar una
    /// Experience de la MISMA Company que el Package — nunca la de otro Provider (UC-P-07 excepción).
    /// Invariante no expresable como FK/CHECK físico (database-design.md), se valida acá en el Service.
    /// </summary>
    private async Task<List<PackageItem>> BuildAndValidateItemsAsync(
        List<PackageItemRequest> items, Guid companyId, CancellationToken ct)
    {
        // Kind llega como string (ver PackageItemRequest) — se parsea una sola vez acá, no en cada uso.
        var parsedItems = items.Select(i => (Request: i, Kind: Enum.Parse<PackageItemKind>(i.Kind))).ToList();

        var experienceIds = parsedItems
            .Where(x => x.Kind == PackageItemKind.EXPERIENCE_REFERENCE && x.Request.ExperienceId.HasValue)
            .Select(x => x.Request.ExperienceId!.Value)
            .Distinct()
            .ToList();

        var experienceById = (await experienceRepository.GetByIdsAsync(experienceIds, ct))
            .ToDictionary(e => e.Id);

        foreach (var (request, kind) in parsedItems.Where(x => x.Kind == PackageItemKind.EXPERIENCE_REFERENCE))
        {
            if (!experienceById.TryGetValue(request.ExperienceId!.Value, out var experience))
                throw new ValidationAppException($"La experiencia referenciada ({request.ExperienceId}) no existe.");

            if (experience.CompanyId != companyId)
                throw new ValidationAppException("Un paquete solo puede incluir experiencias de tu propia empresa.");
        }

        return parsedItems.Select(x => new PackageItem
        {
            Id = Guid.NewGuid(),
            DayNumber = x.Request.DayNumber,
            SortOrder = x.Request.SortOrder,
            Kind = x.Kind,
            ExperienceId = x.Kind == PackageItemKind.EXPERIENCE_REFERENCE ? x.Request.ExperienceId : null,
            Title = NullIfBlank(x.Request.Title),
            Description = NullIfBlank(x.Request.Description)
        }).ToList();
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static PackageResponse ToResponse(Package package) => new()
    {
        Id = package.Id,
        CompanyId = package.CompanyId,
        CompanyName = package.Company!.Name,
        DestinationId = package.DestinationId,
        DestinationName = package.Destination!.Name,
        Title = package.Title,
        Description = package.Description,
        ConditionsText = package.ConditionsText,
        DurationDays = package.DurationDays,
        Price = package.Price,
        Currency = package.Currency,
        Status = package.Status.ToString(),
        Categories = package.Categories
            .Select(c => new CategoryResponse { Id = c.Id, Name = c.Name, Description = c.Description })
            .ToList(),
        Images = package.Images
            .OrderBy(i => i.SortOrder)
            .Select(i => new PackageImageResponse { Id = i.Id, Url = i.Url, SortOrder = i.SortOrder, IsCover = i.IsCover })
            .ToList(),
        Items = package.Items
            .OrderBy(i => i.DayNumber).ThenBy(i => i.SortOrder)
            .Select(i => new PackageItemResponse
            {
                Id = i.Id,
                DayNumber = i.DayNumber,
                SortOrder = i.SortOrder,
                Kind = i.Kind.ToString(),
                ExperienceId = i.ExperienceId,
                Title = i.Title ?? (i.Kind == PackageItemKind.EXPERIENCE_REFERENCE ? i.Experience?.Title : null),
                Description = i.Description
            }).ToList(),
        CreatedAt = package.CreatedAt,
        UpdatedAt = package.UpdatedAt
    };

    private static PackageSummaryResponse ToSummary(Package package) => new()
    {
        Id = package.Id,
        Title = package.Title,
        DestinationName = package.Destination!.Name,
        DurationDays = package.DurationDays,
        Price = package.Price,
        Currency = package.Currency,
        CoverImageUrl = package.Images.FirstOrDefault(i => i.IsCover)?.Url,
        CompanyName = package.Company!.Name,
        Status = package.Status.ToString()
    };
}
