using System.Net;
using System.Net.Http.Json;
using TurisClick.Api.Modules.Categories.Dtos;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>UC-A-05 — Gestionar categorías. Exclusivo de ADMIN.</summary>
[Collection(ApiCollection.Name)]
public class CategoriesEndpointsTests
{
    private readonly TurisClickApiFactory _factory;

    public CategoriesEndpointsTests(TurisClickApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_AsAdmin_Succeeds()
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await LoginAsAdminAsync(client));
        var name = $"Aventura-{Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync("/api/admin/categories", new { name, description = "Actividades al aire libre" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CategoryResponse>(JsonOptions);
        Assert.Equal(name, body!.Name);
    }

    [Fact]
    public async Task Create_DuplicateName_Returns409()
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await LoginAsAdminAsync(client));
        var name = $"Gastronomía-{Guid.NewGuid():N}";

        await client.PostAsJsonAsync("/api/admin/categories", new { name });
        var second = await client.PostAsJsonAsync("/api/admin/categories", new { name });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Delete_AsAdmin_Succeeds()
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await LoginAsAdminAsync(client));
        var createResponse = await client.PostAsJsonAsync("/api/admin/categories", new { name = $"Cultura-{Guid.NewGuid():N}" });
        var created = await createResponse.Content.ReadFromJsonAsync<CategoryResponse>(JsonOptions);

        var deleteResponse = await client.DeleteAsync($"/api/admin/categories/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Create_AsTourist_Returns403()
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await RegisterAndLoginTouristAsync(client, "cat-tourist"));

        var response = await client.PostAsJsonAsync("/api/admin/categories", new { name = "No debería crearse" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
