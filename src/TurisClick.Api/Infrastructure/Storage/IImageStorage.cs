namespace TurisClick.Api.Infrastructure.Storage;

/// <summary>
/// Almacenamiento persistente de imágenes públicas. Devuelve la URL absoluta con la que cualquier cliente
/// (Backoffice, Tourist Mobile) la descarga; esa URL es lo único que se guarda en la base.
/// </summary>
public interface IImageStorage
{
    /// <param name="objectName">Ruta relativa única, ej. "destinations/2026/10/{guid}.webp". Nunca se sobreescribe.</param>
    Task<string> SaveAsync(Stream content, string objectName, string contentType, CancellationToken ct);
}

/// <summary>Excepción de infraestructura: el almacenamiento no está configurado o no respondió.</summary>
public class ImageStorageUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Sin proveedor configurado: la API arranca igual y sólo la subida responde 503.</summary>
public class NotConfiguredImageStorage : IImageStorage
{
    public Task<string> SaveAsync(Stream content, string objectName, string contentType, CancellationToken ct) =>
        throw new ImageStorageUnavailableException("No hay almacenamiento de imágenes configurado (ImageStorage:Provider).");
}
