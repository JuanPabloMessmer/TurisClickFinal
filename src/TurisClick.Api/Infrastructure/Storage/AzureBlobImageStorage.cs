using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace TurisClick.Api.Infrastructure.Storage;

/// <summary>
/// Azure Blob Storage con identidad administrada: ni claves de cuenta ni SAS en la configuración. La
/// identidad del App Service tiene "Storage Blob Data Contributor" sólo sobre el contenedor, y el contenedor
/// admite lectura anónima de blobs (no de listados) para que las apps descarguen las fotos por HTTPS.
/// </summary>
public class AzureBlobImageStorage(
    BlobContainerClient container,
    IOptions<ImageStorageOptions> options,
    ILogger<AzureBlobImageStorage> logger) : IImageStorage
{
    // El nombre de cada blob es único (GUID) y nunca se reescribe: se puede cachear sin vencimiento.
    private const string CacheControl = "public, max-age=31536000, immutable";

    public async Task<string> SaveAsync(Stream content, string objectName, string contentType, CancellationToken ct)
    {
        var blob = container.GetBlobClient(objectName);
        try
        {
            await blob.UploadAsync(content, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType, CacheControl = CacheControl },
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
            }, ct);
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex, "No se pudo subir el blob {BlobName} ({Status} {ErrorCode})", objectName, ex.Status, ex.ErrorCode);
            throw new ImageStorageUnavailableException("Azure Blob Storage rechazó la subida.", ex);
        }

        var publicBase = options.Value.AzureBlob.PublicBaseUrl;
        return string.IsNullOrWhiteSpace(publicBase)
            ? blob.Uri.ToString()
            : $"{publicBase.TrimEnd('/')}/{objectName}";
    }
}
