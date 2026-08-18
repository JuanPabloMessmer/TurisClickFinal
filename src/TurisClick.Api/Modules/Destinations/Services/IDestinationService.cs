using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Destinations.Entities;

namespace TurisClick.Api.Modules.Destinations.Services;

public interface IDestinationService
{
    Task<DestinationResponse> CreateAsync(CreateDestinationRequest request, CancellationToken ct);
    Task<DestinationResponse> UpdateAsync(Guid id, UpdateDestinationRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<DestinationResponse> GetByIdAsync(Guid id, CancellationToken ct);
    Task<List<DestinationResponse>> ListAsync(Guid? parentId, DestinationType? type, CancellationToken ct);
}
