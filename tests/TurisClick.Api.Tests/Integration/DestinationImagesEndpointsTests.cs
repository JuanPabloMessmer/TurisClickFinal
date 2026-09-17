using System.Net;
using System.Net.Http.Json;
using TurisClick.Api.Modules.Destinations.Dtos;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>Imagen representativa de destinos: solo URL (nunca contenido embebido), editable por ADMIN, pública.</summary>
[Collection(ApiCollection.Name)]
public class DestinationImagesEndpointsTests
{
    private const string ImageUrl = "https://upload.wikimedia.org/wikipedia/commons/thumb/a/aa/Example.jpg/1280px-Example.jpg";
    private readonly TurisClickApiFactory _factory;

    public DestinationImagesEndpointsTests(TurisClickApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Admin, DestinationResponse City)> CreateCityAsync(string? imageUrl)
    {
        var admin = _factory.CreateClient();
        UseBearerToken(admin, await LoginAsAdminAsync(admin));
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var country = await (await admin.PostAsJsonAsync("/api/admin/destinations", new { name = $"PaisImg-{suffix}", type = "COUNTRY" }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var region = await (await admin.PostAsJsonAsync("/api/admin/destinations", new { name = $"RegionImg-{suffix}", type = "REGION", parentId = country!.Id }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var response = await admin.PostAsJsonAsync("/api/admin/destinations", new { name = $"CiudadImg-{suffix}", type = "CITY", parentId = region!.Id, imageUrl });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (admin, (await response.Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions))!);
    }

    [Fact]
    public async Task Create_WithImage_IsReturnedByAdminAndPublicEndpoints()
    {
        var (_, city) = await CreateCityAsync(ImageUrl);

        Assert.Equal(ImageUrl, city.ImageUrl);

        var anonymous = _factory.CreateClient();
        var publicList = await anonymous.GetFromJsonAsync<List<PublicDestinationResponse>>("/api/destinations", JsonOptions);
        Assert.Equal(ImageUrl, publicList!.Single(d => d.Id == city.Id).ImageUrl);
        var publicDetail = await anonymous.GetFromJsonAsync<PublicDestinationResponse>($"/api/destinations/{city.Id}", JsonOptions);
        Assert.Equal(ImageUrl, publicDetail!.ImageUrl);
    }

    [Fact]
    public async Task Update_SetsReplacesAndClearsImage_KeepingTheName()
    {
        var (admin, city) = await CreateCityAsync(null);
        Assert.Null(city.ImageUrl);

        var set = await admin.PutAsJsonAsync($"/api/admin/destinations/{city.Id}", new { name = city.Name, imageUrl = ImageUrl });
        Assert.Equal(ImageUrl, (await set.Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions))!.ImageUrl);

        var cleared = await admin.PutAsJsonAsync($"/api/admin/destinations/{city.Id}", new { name = city.Name, imageUrl = (string?)null });
        var body = await cleared.Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        Assert.Null(body!.ImageUrl);
        Assert.Equal(city.Name, body.Name);
    }

    [Theory]
    [InlineData("data:image/png;base64,iVBORw0KGgo=")]
    [InlineData("no-es-una-url")]
    [InlineData("javascript:alert(1)")]
    public async Task InvalidImageUrls_AreRejected(string imageUrl)
    {
        var (admin, city) = await CreateCityAsync(null);

        var response = await admin.PutAsJsonAsync($"/api/admin/destinations/{city.Id}", new { name = city.Name, imageUrl });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TooLongImageUrl_IsRejected()
    {
        var (admin, city) = await CreateCityAsync(null);

        var response = await admin.PutAsJsonAsync($"/api/admin/destinations/{city.Id}",
            new { name = city.Name, imageUrl = "https://example.com/" + new string('a', 500) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NonAdmins_CannotChangeImages()
    {
        var (_, city) = await CreateCityAsync(null);
        var tourist = _factory.CreateClient();
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, "dest-img-tourist"));

        var response = await tourist.PutAsJsonAsync($"/api/admin/destinations/{city.Id}", new { name = city.Name, imageUrl = ImageUrl });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
