using TurisClick.Api.Infrastructure.Storage;

namespace TurisClick.Api.Tests.Support;

/// <summary>Reemplazo de IImageStorage para tests: guarda en memoria y devuelve una URL HTTPS de mentira.</summary>
public class InMemoryImageStorage : IImageStorage
{
    public const string BaseUrl = "https://images.test.turisclick.dev/images";

    public List<StoredImage> Saved { get; } = [];

    public bool Unavailable { get; set; }

    public async Task<string> SaveAsync(Stream content, string objectName, string contentType, CancellationToken ct)
    {
        if (Unavailable)
            throw new ImageStorageUnavailableException("Almacenamiento caído (test).");

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        lock (Saved)
            Saved.Add(new StoredImage(objectName, contentType, buffer.ToArray()));
        return $"{BaseUrl}/{objectName}";
    }

    public record StoredImage(string ObjectName, string ContentType, byte[] Content);
}

/// <summary>Cabeceras mínimas válidas de cada formato, más el resto del archivo de relleno.</summary>
public static class SampleImages
{
    public static byte[] Jpeg(int size = 64) => WithHeader([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01], size);

    public static byte[] Png(int size = 64) => WithHeader([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D], size);

    public static byte[] WebP(int size = 64) => WithHeader("RIFF\x24\0\0\0WEBP"u8.ToArray(), size);

    public static byte[] Gif(int size = 64) => WithHeader("GIF89a"u8.ToArray(), size);

    public static byte[] Svg() => "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray();

    private static byte[] WithHeader(byte[] header, int size)
    {
        var bytes = new byte[Math.Max(size, header.Length)];
        header.CopyTo(bytes, 0);
        return bytes;
    }
}
