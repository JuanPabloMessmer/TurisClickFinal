using Microsoft.AspNetCore.Http;
using TurisClick.Api.Infrastructure.Storage;
using TurisClick.Api.Modules.Destinations.Services;
using TurisClick.Api.Shared.Exceptions;
using TurisClick.Api.Tests.Support;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Destinations;

/// <summary>
/// UC-A-04 — subida de la imagen de un destino. El tipo se decide por la firma del archivo, nunca por el nombre
/// ni por el Content-Type que manda el navegador, y el nombre guardado lo genera el servidor.
/// </summary>
public class DestinationImageServiceTests
{
    private readonly InMemoryImageStorage _storage = new();
    private readonly DestinationImageService _sut;

    public DestinationImageServiceTests() => _sut = new DestinationImageService(_storage);

    private static IFormFile File(byte[] content, string fileName = "foto.jpg", string contentType = "image/jpeg") =>
        new FormFile(new MemoryStream(content), 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    public static TheoryData<string, string, string> ValidImages => new()
    {
        { "jpeg", "image/jpeg", ".jpg" },
        { "png", "image/png", ".png" },
        { "webp", "image/webp", ".webp" },
    };

    [Theory]
    [MemberData(nameof(ValidImages))]
    public async Task UploadAsync_ValidImage_IsStoredWithDetectedTypeAndGeneratedName(string kind, string contentType, string extension)
    {
        var bytes = kind switch { "jpeg" => SampleImages.Jpeg(), "png" => SampleImages.Png(), _ => SampleImages.WebP() };

        var result = await _sut.UploadAsync(File(bytes, "../../mi foto <script>.jpg"), CancellationToken.None);

        var saved = Assert.Single(_storage.Saved);
        Assert.Equal(contentType, saved.ContentType);
        Assert.Equal(bytes, saved.Content);
        Assert.Matches(@$"^destinations/\d{{4}}/\d{{2}}/[0-9a-f]{{32}}\{extension}$", saved.ObjectName);
        Assert.Equal($"{InMemoryImageStorage.BaseUrl}/{saved.ObjectName}", result.Url);
        Assert.DoesNotContain("script", result.Url);
    }

    [Fact]
    public async Task UploadAsync_PngWithJpgNameAndMime_IsStoredAsPng()
    {
        await _sut.UploadAsync(File(SampleImages.Png(), "foto.jpg", "image/jpeg"), CancellationToken.None);

        var saved = Assert.Single(_storage.Saved);
        Assert.Equal("image/png", saved.ContentType);
        Assert.EndsWith(".png", saved.ObjectName);
    }

    [Fact]
    public async Task UploadAsync_WithoutFile_ThrowsSpanishValidation()
    {
        var ex = await Assert.ThrowsAsync<ValidationAppException>(() => _sut.UploadAsync(null, CancellationToken.None));
        Assert.Equal(DestinationImageService.MissingFileMessage, ex.Message);

        ex = await Assert.ThrowsAsync<ValidationAppException>(() => _sut.UploadAsync(File([]), CancellationToken.None));
        Assert.Equal(DestinationImageService.MissingFileMessage, ex.Message);
        Assert.Empty(_storage.Saved);
    }

    [Fact]
    public async Task UploadAsync_MoreThan5Mb_IsRejected()
    {
        var file = File(SampleImages.Jpeg((int)DestinationImageService.MaxBytes + 1));

        var ex = await Assert.ThrowsAsync<ValidationAppException>(() => _sut.UploadAsync(file, CancellationToken.None));

        Assert.Equal(DestinationImageService.TooLargeMessage, ex.Message);
        Assert.Empty(_storage.Saved);
    }

    [Fact]
    public async Task UploadAsync_Exactly5Mb_IsAccepted()
    {
        await _sut.UploadAsync(File(SampleImages.Jpeg((int)DestinationImageService.MaxBytes)), CancellationToken.None);

        Assert.Single(_storage.Saved);
    }

    public static TheoryData<byte[], string, string> UnsupportedFiles => new()
    {
        { SampleImages.Gif(), "animada.gif", "image/gif" },
        // Disfrazados de JPG: el nombre y el Content-Type no alcanzan para pasar.
        { SampleImages.Svg(), "logo.jpg", "image/jpeg" },
        { "<html><body>hola</body></html>"u8.ToArray(), "pagina.png", "image/png" },
        { "%PDF-1.7"u8.ToArray(), "doc.webp", "image/webp" },
        { new byte[] { 0xFF, 0xD8 }, "cortada.jpg", "image/jpeg" },
    };

    [Theory]
    [MemberData(nameof(UnsupportedFiles))]
    public async Task UploadAsync_NotJpgPngOrWebp_IsRejected(byte[] content, string fileName, string contentType)
    {
        var ex = await Assert.ThrowsAsync<ValidationAppException>(
            () => _sut.UploadAsync(File(content, fileName, contentType), CancellationToken.None));

        Assert.Equal(DestinationImageService.UnsupportedFormatMessage, ex.Message);
        Assert.Empty(_storage.Saved);
    }

    [Fact]
    public async Task UploadAsync_StorageUnavailable_Throws503WithoutInternals()
    {
        _storage.Unavailable = true;

        var ex = await Assert.ThrowsAsync<ServiceUnavailableAppException>(
            () => _sut.UploadAsync(File(SampleImages.Jpeg()), CancellationToken.None));

        Assert.Equal(DestinationImageService.StorageUnavailableMessage, ex.Message);
    }

    [Fact]
    public async Task UploadAsync_StorageNotConfigured_Throws503()
    {
        var sut = new DestinationImageService(new NotConfiguredImageStorage());

        await Assert.ThrowsAsync<ServiceUnavailableAppException>(
            () => sut.UploadAsync(File(SampleImages.Jpeg()), CancellationToken.None));
    }
}
