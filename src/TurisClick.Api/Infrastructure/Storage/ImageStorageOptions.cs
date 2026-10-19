namespace TurisClick.Api.Infrastructure.Storage;

/// <summary>
/// Dónde se guardan las imágenes que sube el Backoffice. Nunca en el directorio de despliegue del App
/// Service: un redeploy lo reemplaza y las fotos se perderían. Ver infra/README.md, "Imágenes subidas".
/// </summary>
public class ImageStorageOptions
{
    public const string SectionName = "ImageStorage";

    /// <summary>"None" (default: la subida responde 503), "AzureBlob" o "Local" (sólo Development).</summary>
    public string Provider { get; set; } = ImageStorageProviders.None;

    public AzureBlobImageStorageOptions AzureBlob { get; set; } = new();

    public LocalImageStorageOptions Local { get; set; } = new();
}

public static class ImageStorageProviders
{
    public const string None = "None";
    public const string AzureBlob = "AzureBlob";
    public const string Local = "Local";
}

public class AzureBlobImageStorageOptions
{
    /// <summary>Endpoint HTTPS de la cuenta, ej. https://stturisclickv2img.blob.core.windows.net. Sin claves: acceso por identidad.</summary>
    public string? ServiceUri { get; set; }

    public string ContainerName { get; set; } = "images";

    /// <summary>Client ID de la identidad user-assigned del App Service. Vacío: DefaultAzureCredential (az login en local).</summary>
    public string? ManagedIdentityClientId { get; set; }

    /// <summary>Opcional: base pública distinta del endpoint de blobs (ej. un CDN delante del contenedor).</summary>
    public string? PublicBaseUrl { get; set; }
}

public class LocalImageStorageOptions
{
    /// <summary>Carpeta absoluta fuera del repo. Vacía: %LOCALAPPDATA%/TurisClick/uploads.</summary>
    public string? RootPath { get; set; }

    /// <summary>Ruta bajo la que la API sirve los archivos guardados.</summary>
    public string RequestPath { get; set; } = "/uploads";
}
