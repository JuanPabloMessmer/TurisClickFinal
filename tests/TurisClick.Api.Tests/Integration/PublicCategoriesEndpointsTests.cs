using System.Net;
using System.Net.Http.Json;
using TurisClick.Api.Modules.Categories.Dtos;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// Lectura pública del catálogo de categorías. Existe para que un cliente anónimo (Tourist Mobile)
/// pueda descubrir las categorías por las que UC-T-04/06 ya permiten filtrar; la gestión sigue siendo
/// exclusiva de ADMIN en /api/admin/categories.
/// </summary>
[Collection(ApiCollection.Name)]
public class PublicCategoriesEndpointsTests
{
    private readonly TurisClickApiFactory _factory;

    public PublicCategoriesEndpointsTests(TurisClickApiFactory factory) => _factory = factory;

    private async Task<CategoryResponse> CreateCategoryAsync(string name)
    {
        var adminClient = _factory.CreateClient();
        UseBearerToken(adminClient, await LoginAsAdminAsync(adminClient));

        var response = await adminClient.PostAsJsonAsync("/api/admin/categories", new { name, description = "Categoría de prueba" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CategoryResponse>(JsonOptions))!;
    }

    [Fact]
    public async Task List_WithoutAuthentication_ReturnsCategories()
    {
        var created = await CreateCategoryAsync($"PublicaCat{Guid.NewGuid().ToString("N")[..8]}");

        // Cliente anónimo: sin token, como el catálogo público del mobile antes de iniciar sesión.
        var response = await _factory.CreateClient().GetAsync("/api/categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<CategoryResponse>>(JsonOptions);
        Assert.Contains(body!, c => c.Id == created.Id && c.Name == created.Name);
    }

    [Fact]
    public async Task List_ExposesOnlyTheFieldsTheCatalogNeeds()
    {
        var created = await CreateCategoryAsync($"CamposCat{Guid.NewGuid().ToString("N")[..8]}");

        var body = await (await _factory.CreateClient().GetAsync("/api/categories"))
            .Content.ReadFromJsonAsync<List<CategoryResponse>>(JsonOptions);

        var category = Assert.Single(body!, c => c.Id == created.Id);
        Assert.Equal(created.Name, category.Name);
        Assert.Equal("Categoría de prueba", category.Description);
    }

    [Fact]
    public async Task List_ReturnedIdsCanBeUsedToFilterTheCatalog()
    {
        // El endpoint existe justamente para esto: sin él, un anónimo puede filtrar por categoryId pero
        // no tiene forma de averiguar qué ids existen.
        var created = await CreateCategoryAsync($"FiltroCat{Guid.NewGuid().ToString("N")[..8]}");
        var publicClient = _factory.CreateClient();

        var categories = await (await publicClient.GetAsync("/api/categories"))
            .Content.ReadFromJsonAsync<List<CategoryResponse>>(JsonOptions);
        var categoryId = Assert.Single(categories!, c => c.Id == created.Id).Id;

        Assert.Equal(HttpStatusCode.OK, (await publicClient.GetAsync($"/api/experiences?categoryId={categoryId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await publicClient.GetAsync($"/api/packages?categoryId={categoryId}")).StatusCode);
    }

    [Fact]
    public async Task ManagementEndpointsStayAdminOnly()
    {
        var anonymous = _factory.CreateClient();

        // Solo se abrió la lectura de la lista pública: el CRUD de administración sigue cerrado...
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/admin/categories", new { name = "No permitido" })).StatusCode);

        // ...y la ruta pública no acepta escrituras en absoluto (no existe ese verbo).
        var write = await anonymous.PostAsJsonAsync("/api/categories", new { name = "No permitido" });
        Assert.NotEqual(HttpStatusCode.Created, write.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, write.StatusCode);
    }
}
