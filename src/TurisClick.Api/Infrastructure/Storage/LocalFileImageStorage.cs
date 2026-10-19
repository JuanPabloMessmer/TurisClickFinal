namespace TurisClick.Api.Infrastructure.Storage;

/// <summary>
/// Sólo para desarrollo local sin Azure: guarda en una carpeta fuera del repo y la API la sirve en
/// ImageStorage:Local:RequestPath. StorageServiceCollectionExtensions impide usarlo fuera de Development.
/// </summary>
public class LocalFileImageStorage(string rootPath, string requestPath, IHttpContextAccessor httpContextAccessor) : IImageStorage
{
    public async Task<string> SaveAsync(Stream content, string objectName, string contentType, CancellationToken ct)
    {
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, objectName));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath)) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.Ordinal))
            throw new InvalidOperationException("Nombre de objeto inválido.");

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using (var file = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write))
            await content.CopyToAsync(file, ct);

        var request = httpContextAccessor.HttpContext?.Request
            ?? throw new InvalidOperationException("LocalFileImageStorage necesita una request HTTP para armar la URL.");
        return $"{request.Scheme}://{request.Host}{requestPath.TrimEnd('/')}/{objectName}";
    }
}
