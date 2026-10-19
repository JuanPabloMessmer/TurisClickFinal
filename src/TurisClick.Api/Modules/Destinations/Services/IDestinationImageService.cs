using TurisClick.Api.Modules.Destinations.Dtos;

namespace TurisClick.Api.Modules.Destinations.Services;

public interface IDestinationImageService
{
    Task<UploadedImageResponse> UploadAsync(IFormFile? file, CancellationToken ct);
}
