using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using TurisClick.Api.Infrastructure.Storage;
using Xunit;

namespace TurisClick.Api.Tests.Infrastructure.Storage;

/// <summary>
/// Qué almacenamiento queda registrado según ImageStorage:Provider. Lo importante: el disco local nunca se usa
/// fuera de Development (en App Service las fotos se perderían en el próximo deploy) y Azure exige HTTPS.
/// </summary>
public class ImageStorageRegistrationTests
{
    private static IServiceProvider Build(Dictionary<string, string?> settings, string environment = "Production")
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var host = new Mock<IHostEnvironment>();
        host.SetupGet(h => h.EnvironmentName).Returns(environment);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddImageStorage(configuration, host.Object);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void WithoutConfiguration_UploadsAreDisabledButTheApiStarts()
    {
        var storage = Build([]).GetRequiredService<IImageStorage>();

        Assert.IsType<NotConfiguredImageStorage>(storage);
    }

    [Fact]
    public void AzureBlob_WithHttpsEndpoint_UsesBlobStorage()
    {
        var provider = Build(new()
        {
            ["ImageStorage:Provider"] = "AzureBlob",
            ["ImageStorage:AzureBlob:ServiceUri"] = "https://stturisclickv2imgjpm.blob.core.windows.net/",
            ["ImageStorage:AzureBlob:ContainerName"] = "images",
            ["ImageStorage:AzureBlob:ManagedIdentityClientId"] = Guid.NewGuid().ToString(),
        });

        Assert.IsType<AzureBlobImageStorage>(provider.GetRequiredService<IImageStorage>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://stturisclickv2imgjpm.blob.core.windows.net/")]
    [InlineData("no-es-una-url")]
    public void AzureBlob_WithoutHttpsEndpoint_FailsOnStartup(string? serviceUri)
    {
        var provider = Build(new()
        {
            ["ImageStorage:Provider"] = "AzureBlob",
            ["ImageStorage:AzureBlob:ServiceUri"] = serviceUri,
        });

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IImageStorage>());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Testing")]
    public void Local_OutsideDevelopment_IsRefused(string environment)
    {
        Assert.Throws<InvalidOperationException>(() => Build(new() { ["ImageStorage:Provider"] = "Local" }, environment));
    }

    [Fact]
    public async Task Local_InDevelopment_SavesOutsideTheAppAndReturnsAnApiUrl()
    {
        var root = Path.Combine(Path.GetTempPath(), $"turisclick-uploads-{Guid.NewGuid():N}");
        try
        {
            var provider = Build(new()
            {
                ["ImageStorage:Provider"] = "Local",
                ["ImageStorage:Local:RootPath"] = root,
            }, environment: "Development");

            var context = new DefaultHttpContext();
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("localhost", 7131);
            provider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;

            var storage = provider.GetRequiredService<IImageStorage>();
            var url = await storage.SaveAsync(new MemoryStream([1, 2, 3]), "destinations/2026/10/abc.jpg", "image/jpeg", CancellationToken.None);

            Assert.Equal("https://localhost:7131/uploads/destinations/2026/10/abc.jpg", url);
            Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(Path.Combine(root, "destinations", "2026", "10", "abc.jpg")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Local_RejectsObjectNamesThatEscapeTheRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"turisclick-uploads-{Guid.NewGuid():N}");
        var storage = new LocalFileImageStorage(root, "/uploads", new HttpContextAccessor());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => storage.SaveAsync(new MemoryStream([1]), "../fuera.jpg", "image/jpeg", CancellationToken.None));
    }

    [Fact]
    public void ImageFormat_DetectsOnlyJpgPngAndWebp()
    {
        Assert.Equal(ImageFormat.Jpeg, ImageFormat.Detect(SampleHeader(0xFF, 0xD8, 0xFF, 0xDB)));
        Assert.Equal(ImageFormat.Png, ImageFormat.Detect(SampleHeader(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)));
        Assert.Equal(ImageFormat.WebP, ImageFormat.Detect("RIFF\0\0\0\0WEBP"u8));
        Assert.Null(ImageFormat.Detect("RIFF\0\0\0\0WAVE"u8));
        Assert.Null(ImageFormat.Detect("GIF89a"u8));
        Assert.Null(ImageFormat.Detect([]));
    }

    private static byte[] SampleHeader(params byte[] bytes) => bytes;
}
