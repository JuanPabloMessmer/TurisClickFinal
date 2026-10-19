using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace TurisClick.Api.Infrastructure.Storage;

public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// ImageStorage:Provider elige la implementación al arrancar. Una configuración incompleta falla acá, no
    /// en la primera subida; la ausencia de configuración no: la API arranca y sólo la subida responde 503.
    /// </summary>
    public static IServiceCollection AddImageStorage(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection(ImageStorageOptions.SectionName);
        services.Configure<ImageStorageOptions>(section);
        var options = section.Get<ImageStorageOptions>() ?? new ImageStorageOptions();

        switch (options.Provider)
        {
            case var p when string.Equals(p, ImageStorageProviders.AzureBlob, StringComparison.OrdinalIgnoreCase):
                services.AddSingleton(_ => CreateContainerClient(options.AzureBlob));
                services.AddSingleton<IImageStorage, AzureBlobImageStorage>();
                break;

            case var p when string.Equals(p, ImageStorageProviders.Local, StringComparison.OrdinalIgnoreCase):
                // En App Service el disco local es el directorio de despliegue o uno efímero: las fotos se perderían.
                if (!environment.IsDevelopment())
                    throw new InvalidOperationException("ImageStorage:Provider=Local sólo se admite en Development. En Azure usá AzureBlob.");
                services.AddHttpContextAccessor();
                services.AddSingleton<IImageStorage>(sp => new LocalFileImageStorage(
                    ResolveLocalRoot(options.Local), options.Local.RequestPath, sp.GetRequiredService<IHttpContextAccessor>()));
                break;

            default:
                services.AddSingleton<IImageStorage, NotConfiguredImageStorage>();
                break;
        }

        return services;
    }

    /// <summary>Con el proveedor Local, la API sirve la carpeta de subidas; con cualquier otro no hace nada.</summary>
    public static WebApplication UseLocalImageStorageFiles(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<ImageStorageOptions>>().Value;
        if (!string.Equals(options.Provider, ImageStorageProviders.Local, StringComparison.OrdinalIgnoreCase))
            return app;

        var root = ResolveLocalRoot(options.Local);
        Directory.CreateDirectory(root);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(root),
            RequestPath = options.Local.RequestPath
        });
        return app;
    }

    private static BlobContainerClient CreateContainerClient(AzureBlobImageStorageOptions options)
    {
        if (!Uri.TryCreate(options.ServiceUri, UriKind.Absolute, out var serviceUri) || serviceUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("ImageStorage:AzureBlob:ServiceUri debe ser la URL https:// de la cuenta de almacenamiento.");
        if (string.IsNullOrWhiteSpace(options.ContainerName))
            throw new InvalidOperationException("Falta ImageStorage:AzureBlob:ContainerName.");

        // Identidad administrada en Azure; en local, DefaultAzureCredential toma la sesión de `az login`.
        TokenCredential credential = string.IsNullOrWhiteSpace(options.ManagedIdentityClientId)
            ? new DefaultAzureCredential()
            : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(options.ManagedIdentityClientId));

        return new BlobServiceClient(serviceUri, credential).GetBlobContainerClient(options.ContainerName);
    }

    private static string ResolveLocalRoot(LocalImageStorageOptions options) =>
        string.IsNullOrWhiteSpace(options.RootPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TurisClick", "uploads")
            : Path.GetFullPath(options.RootPath);
}
