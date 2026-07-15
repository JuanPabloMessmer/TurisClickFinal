using TurisClick.Api.Modules.Destinations.DTOs;

namespace TurisClick.Api.Modules.Destinations.Services;

public interface IDestinationService
{
    Task<IReadOnlyList<DestinationResponse>> GetAllAsync();
    Task<DestinationResponse> GetByIdAsync(int destinationId);
    Task<DestinationResponse> CreateAsync(CreateDestinationRequest request);
    Task<DestinationResponse> UpdateAsync(int destinationId, UpdateDestinationRequest request);
    Task<DestinationResponse> ChangeStatusAsync(int destinationId, ChangeDestinationStatusRequest request);
}
