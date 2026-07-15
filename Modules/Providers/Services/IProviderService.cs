using TurisClick.Api.Modules.Providers.DTOs;

namespace TurisClick.Api.Modules.Providers.Services;

public interface IProviderService
{
    Task<IReadOnlyList<ProviderResponse>> GetAllAsync();
    Task<ProviderResponse> GetByIdAsync(int providerId);
    Task<ProviderResponse> CreateAsync(CreateProviderRequest request);
    Task<ProviderResponse> UpdateAsync(int providerId, UpdateProviderRequest request);
    Task<ProviderResponse> ChangeStatusAsync(int providerId, ChangeProviderStatusRequest request);
    Task<ProviderResponse> GetProfileAsync(int userId);
    Task<ProviderResponse> UpdateProfileAsync(int userId, UpdateProviderRequest request);
}
