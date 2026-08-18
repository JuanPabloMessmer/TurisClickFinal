using TurisClick.Api.Modules.Destinations.Entities;

namespace TurisClick.Api.Modules.Destinations.Repositories;

public interface IDestinationRepository
{
    Task<Destination?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Destination?> GetByIdWithParentAsync(Guid id, CancellationToken ct);
    Task<List<Destination>> ListAsync(Guid? parentId, DestinationType? type, CancellationToken ct);
    Task<bool> ExistsWithNameAsync(string name, Guid? parentId, DestinationType type, Guid? excludeId, CancellationToken ct);
    Task<bool> HasChildrenAsync(Guid id, CancellationToken ct);
    Task AddAsync(Destination destination, CancellationToken ct);
    void Remove(Destination destination);
}
