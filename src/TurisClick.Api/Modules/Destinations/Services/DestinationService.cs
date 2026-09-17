using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Destinations.Entities;
using TurisClick.Api.Modules.Destinations.Repositories;
using TurisClick.Api.Modules.Experiences.Repositories;
using TurisClick.Api.Modules.Packages.Repositories;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Destinations.Services;

/// <summary>
/// Depende de IExperienceRepository/IPackageRepository (normalmente el flujo de dependencias entre
/// módulos va al revés — Experiences/Packages dependen de Destinations) únicamente para poder devolver
/// un 409 de dominio claro en DeleteAsync antes de que la FK física falle en Postgres. Es la única razón
/// de este acoplamiento cruzado; no se usa para nada más acá.
/// </summary>
public class DestinationService(
    IDestinationRepository destinationRepository,
    IExperienceRepository experienceRepository,
    IPackageRepository packageRepository,
    TurisClickDbContext db) : IDestinationService
{
    public async Task<DestinationResponse> CreateAsync(CreateDestinationRequest request, CancellationToken ct)
    {
        var type = Enum.Parse<DestinationType>(request.Type);
        var name = request.Name.Trim();

        Destination? parent = null;
        if (request.ParentId is { } parentId)
        {
            parent = await destinationRepository.GetByIdAsync(parentId, ct)
                ?? throw new NotFoundAppException("El destino padre indicado no existe.");
        }

        EnsureValidHierarchy(type, parent);

        if (await destinationRepository.ExistsWithNameAsync(name, request.ParentId, type, excludeId: null, ct))
            throw new ConflictAppException("Ya existe un destino con ese nombre en el mismo nivel.");

        var destination = new Destination
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = type,
            ParentId = request.ParentId,
            ImageUrl = NullIfBlank(request.ImageUrl),
            CreatedAt = DateTimeOffset.UtcNow
        };

        await destinationRepository.AddAsync(destination, ct);
        await db.SaveChangesAsync(ct);

        return ToResponse(destination, parent);
    }

    public async Task<DestinationResponse> UpdateAsync(Guid id, UpdateDestinationRequest request, CancellationToken ct)
    {
        var destination = await destinationRepository.GetByIdWithParentAsync(id, ct)
            ?? throw new NotFoundAppException("Destino no encontrado.");

        var name = request.Name.Trim();

        if (await destinationRepository.ExistsWithNameAsync(name, destination.ParentId, destination.Type, excludeId: id, ct))
            throw new ConflictAppException("Ya existe un destino con ese nombre en el mismo nivel.");

        destination.Name = name;
        destination.ImageUrl = NullIfBlank(request.ImageUrl);
        await db.SaveChangesAsync(ct);

        return ToResponse(destination, destination.Parent);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var destination = await destinationRepository.GetByIdAsync(id, ct)
            ?? throw new NotFoundAppException("Destino no encontrado.");

        if (await destinationRepository.HasChildrenAsync(id, ct))
            throw new ConflictAppException("No se puede eliminar un destino que tiene destinos hijos.");

        // UC-A-04: chequeo explícito antes de intentar el DELETE físico — sin esto, Postgres rechaza el
        // UPDATE/DELETE por la FK Restrict de experiences.destination_id/packages.destination_id y EF
        // Core lo envuelve en un DbUpdateException genérico ("An error occurred while saving the entity
        // changes..."), que el GlobalExceptionHandler devolvía tal cual como 500 sin ningún detalle útil.
        if (await experienceRepository.ExistsForDestinationAsync(id, ct) || await packageRepository.ExistsForDestinationAsync(id, ct))
            throw new ConflictAppException("No se puede eliminar el destino porque está siendo utilizado por experiencias o paquetes.");

        destinationRepository.Remove(destination);
        await db.SaveChangesAsync(ct);
    }

    public async Task<DestinationResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var destination = await destinationRepository.GetByIdWithParentAsync(id, ct)
            ?? throw new NotFoundAppException("Destino no encontrado.");

        return ToResponse(destination, destination.Parent);
    }

    public async Task<List<DestinationResponse>> ListAsync(Guid? parentId, DestinationType? type, CancellationToken ct)
    {
        var destinations = await destinationRepository.ListAsync(parentId, type, ct);
        return destinations.Select(d => ToResponse(d, d.Parent)).ToList();
    }

    /// <summary>docs/domain-model.md regla 10: la jerarquía es estrictamente Country → Region → City.</summary>
    private static void EnsureValidHierarchy(DestinationType type, Destination? parent)
    {
        switch (type)
        {
            case DestinationType.COUNTRY when parent is not null:
                throw new ValidationAppException("Un destino de tipo COUNTRY no puede tener padre.");
            case DestinationType.REGION when parent is null or { Type: not DestinationType.COUNTRY }:
                throw new ValidationAppException("Un destino de tipo REGION requiere un padre de tipo COUNTRY.");
            case DestinationType.CITY when parent is null or { Type: not DestinationType.REGION }:
                throw new ValidationAppException("Un destino de tipo CITY requiere un padre de tipo REGION.");
        }
    }

    private static DestinationResponse ToResponse(Destination destination, Destination? parent) => new()
    {
        Id = destination.Id,
        Name = destination.Name,
        Type = destination.Type.ToString(),
        ParentId = destination.ParentId,
        ParentName = parent?.Name,
        ImageUrl = destination.ImageUrl
    };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
