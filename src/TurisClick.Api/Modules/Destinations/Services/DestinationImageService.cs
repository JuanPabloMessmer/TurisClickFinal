using TurisClick.Api.Infrastructure.Storage;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Destinations.Services;

/// <summary>UC-A-04 — Imagen representativa de un destino, subida desde el Backoffice.</summary>
public class DestinationImageService(IImageStorage storage) : IDestinationImageService
{
    public const long MaxBytes = 5 * 1024 * 1024;

    public const string MissingFileMessage = "Seleccioná una imagen para subir.";
    public const string TooLargeMessage = "La imagen pesa más de 5 MB. Elegí una imagen más liviana.";
    public const string UnsupportedFormatMessage = "Formato no admitido. Subí una imagen JPG, PNG o WebP.";
    public const string StorageUnavailableMessage = "No se pudo guardar la imagen en este momento. Intentá de nuevo en unos minutos.";

    public async Task<UploadedImageResponse> UploadAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw new ValidationAppException(MissingFileMessage);
        if (file.Length > MaxBytes)
            throw new ValidationAppException(TooLargeMessage);

        await using var content = file.OpenReadStream();

        var header = new byte[ImageFormat.SignatureLength];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        var format = ImageFormat.Detect(header.AsSpan(0, read))
            ?? throw new ValidationAppException(UnsupportedFormatMessage);
        content.Position = 0;

        // Nombre generado por el servidor: el del archivo original nunca llega al almacenamiento ni a la URL.
        var now = DateTimeOffset.UtcNow;
        var objectName = $"destinations/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{format.Extension}";

        try
        {
            var url = await storage.SaveAsync(content, objectName, format.ContentType, ct);
            return new UploadedImageResponse { Url = url };
        }
        catch (ImageStorageUnavailableException)
        {
            throw new ServiceUnavailableAppException(StorageUnavailableMessage);
        }
    }
}
