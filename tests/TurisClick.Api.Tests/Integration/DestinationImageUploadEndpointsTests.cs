using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TurisClick.Api.Infrastructure.Storage;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Destinations.Services;
using TurisClick.Api.Tests.Support;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// UC-A-04 — subida de la imagen de un destino desde el Backoffice. La subida sólo devuelve la URL; el destino se
/// actualiza con el POST/PUT de siempre, así que la app móvil la sigue leyendo de ImageUrl sin cambios.
/// </summary>
[Collection(ApiCollection.Name)]
public class DestinationImageUploadEndpointsTests
{
    private const string UploadPath = "/api/admin/destinations/images";
    private readonly TurisClickApiFactory _factory;

    public DestinationImageUploadEndpointsTests(TurisClickApiFactory factory) => _factory = factory;

    private (WebApplicationFactory<Program> App, InMemoryImageStorage Storage) WithStorage()
    {
        var storage = new InMemoryImageStorage();
        var app = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IImageStorage>(storage)));
        return (app, storage);
    }

    private static MultipartFormDataContent Form(byte[] content, string fileName = "foto.jpg", string contentType = "image/jpeg")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static async Task<HttpClient> AdminAsync(WebApplicationFactory<Program> app)
    {
        var admin = app.CreateClient();
        UseBearerToken(admin, await LoginAsAdminAsync(admin));
        return admin;
    }

    private static async Task<string?> DetailAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("detail").GetString();
    }

    [Fact]
    public async Task Admin_UploadsImage_AndCreatesAndReplacesADestinationWithIt()
    {
        var (app, storage) = WithStorage();
        using var _ = app;
        var admin = await AdminAsync(app);

        var upload = await admin.PostAsync(UploadPath, Form(SampleImages.WebP(), "uyuni.webp", "image/webp"));
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var uploaded = (await upload.Content.ReadFromJsonAsync<UploadedImageResponse>(JsonOptions))!;
        Assert.StartsWith("https://", uploaded.Url);
        Assert.Equal("image/webp", Assert.Single(storage.Saved).ContentType);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var created = await admin.PostAsJsonAsync("/api/admin/destinations", new { name = $"PaisSubida-{suffix}", type = "COUNTRY", imageUrl = uploaded.Url });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var country = (await created.Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions))!;
        Assert.Equal(uploaded.Url, country.ImageUrl);

        // El catálogo público (lo que lee la app móvil) devuelve la misma URL.
        var publicDetail = await app.CreateClient().GetFromJsonAsync<PublicDestinationResponse>($"/api/destinations/{country.Id}", JsonOptions);
        Assert.Equal(uploaded.Url, publicDetail!.ImageUrl);

        // Reemplazo: otra subida y un PUT con la nueva URL.
        var replacement = (await (await admin.PostAsync(UploadPath, Form(SampleImages.Png(), "nueva.png", "image/png")))
            .Content.ReadFromJsonAsync<UploadedImageResponse>(JsonOptions))!;
        Assert.NotEqual(uploaded.Url, replacement.Url);
        var updated = await admin.PutAsJsonAsync($"/api/admin/destinations/{country.Id}", new { name = country.Name, imageUrl = replacement.Url });
        Assert.Equal(replacement.Url, (await updated.Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions))!.ImageUrl);
    }

    [Fact]
    public async Task PreviouslyStoredExternalUrls_StillWork()
    {
        var admin = await AdminAsync(_factory);
        const string wikimedia = "https://upload.wikimedia.org/wikipedia/commons/thumb/a/aa/Example.jpg/1280px-Example.jpg";

        var created = await admin.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"PaisExterno-{Guid.NewGuid():N}"[..20], type = "COUNTRY", imageUrl = wikimedia });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(wikimedia, (await created.Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions))!.ImageUrl);
    }

    [Fact]
    public async Task Anonymous_IsRejectedWith401()
    {
        var (app, storage) = WithStorage();
        using var _ = app;

        var response = await app.CreateClient().PostAsync(UploadPath, Form(SampleImages.Jpeg()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(storage.Saved);
    }

    [Fact]
    public async Task NonAdmins_AreRejectedWith403()
    {
        var (app, storage) = WithStorage();
        using var _ = app;
        var tourist = app.CreateClient();
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, "dest-upload-tourist"));
        var provider = app.CreateClient();
        UseBearerToken(provider, (await RegisterProviderAsync(provider, "dest-upload-provider")).AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await tourist.PostAsync(UploadPath, Form(SampleImages.Jpeg()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await provider.PostAsync(UploadPath, Form(SampleImages.Jpeg()))).StatusCode);
        Assert.Empty(storage.Saved);
    }

    [Fact]
    public async Task UnsupportedFile_Returns400InSpanish()
    {
        var (app, storage) = WithStorage();
        using var _ = app;
        var admin = await AdminAsync(app);

        var response = await admin.PostAsync(UploadPath, Form(SampleImages.Svg(), "logo.jpg", "image/jpeg"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DestinationImageService.UnsupportedFormatMessage, await DetailAsync(response));
        Assert.Empty(storage.Saved);
    }

    [Fact]
    public async Task TooLargeFile_Returns400InSpanish()
    {
        var (app, storage) = WithStorage();
        using var _ = app;
        var admin = await AdminAsync(app);

        var response = await admin.PostAsync(UploadPath, Form(SampleImages.Jpeg((int)DestinationImageService.MaxBytes + 1024)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DestinationImageService.TooLargeMessage, await DetailAsync(response));
        Assert.Empty(storage.Saved);
    }

    [Fact]
    public async Task MissingFile_Returns400InSpanish()
    {
        var (app, _) = WithStorage();
        using var __ = app;
        var admin = await AdminAsync(app);

        var response = await admin.PostAsync(UploadPath, new MultipartFormDataContent { { new StringContent("x"), "otro" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DestinationImageService.MissingFileMessage, await DetailAsync(response));
    }

    [Fact]
    public async Task WithoutConfiguredStorage_Returns503()
    {
        // La factory de tests no configura ImageStorage: es el caso de un ambiente sin la cuenta creada.
        var admin = await AdminAsync(_factory);

        var response = await admin.PostAsync(UploadPath, Form(SampleImages.Jpeg()));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(DestinationImageService.StorageUnavailableMessage, await DetailAsync(response));
    }
}
