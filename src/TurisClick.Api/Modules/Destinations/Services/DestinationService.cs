using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Destinations.Entities;
using TurisClick.Api.Modules.Destinations.Repositories;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Destinations.Services;

public class DestinationService(IDestinationRepository destinationRepository, TurisClickDbContext db) : IDestinationService
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
        await db.SaveChangesAsync(ct);

        return ToResponse(destination, destination.Parent);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var destination = await destinationRepository.GetByIdAsync(id, ct)
            ?? throw new NotFoundAppException("Destino no encontrado.");

        if (await destinationRepository.HasChildrenAsync(id, ct))
            throw new ConflictAppException("No se puede eliminar un destino que tiene destinos hijos.");

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
        ParentName = parent?.Name
    };
}
