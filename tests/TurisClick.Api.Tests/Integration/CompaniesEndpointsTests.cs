using System.Net;
using System.Net.Http.Json;
using TurisClick.Api.Modules.Companies.Dtos;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>UC-P-02 — Gestionar perfil de "Mi Empresa". Alcance: solo la empresa del PROVIDER autenticado.</summary>
[Collection(ApiCollection.Name)]
public class CompaniesEndpointsTests
{
    private readonly TurisClickApiFactory _factory;

    public CompaniesEndpointsTests(TurisClickApiFactory factory) => _factory = factory;

    [Fact]
    public async Task GetMyCompany_WhilePending_ReturnsOwnCompanyStatus()
    {
        var client = _factory.CreateClient();
        var provider = await RegisterProviderAsync(client, "mycompany-get");
        UseBearerToken(client, provider.AccessToken);

        var response = await client.GetAsync("/api/companies/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CompanyResponse>(JsonOptions);
        Assert.Equal(provider.Company.Id, body!.Id);
        Assert.Equal("PENDING_APPROVAL", body.Status);
    }

    [Fact]
    public async Task UpdateMyCompany_WhilePending_Returns409()
    {
        var client = _factory.CreateClient();
        var provider = await RegisterProviderAsync(client, "mycompany-pending-put");
        UseBearerToken(client, provider.AccessToken);

        var response = await client.PutAsJsonAsync("/api/companies/me", new
        {
            name = "Nombre actualizado",
            contactEmail = "nuevo-contacto@turisclick.dev"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMyCompany_AfterApproval_Succeeds()
    {
        var client = _factory.CreateClient();
        var provider = await RegisterProviderAsync(client, "mycompany-approved-put");

        var adminClient = _factory.CreateClient();
        UseBearerToken(adminClient, await LoginAsAdminAsync(adminClient));
        var approveResponse = await adminClient.PostAsync($"/api/admin/companies/{provider.Company.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        UseBearerToken(client, provider.AccessToken);
        var updateResponse = await client.PutAsJsonAsync("/api/companies/me", new
        {
            name = "Andes Travel Renovada",
            description = "Nueva descripción",
            contactEmail = "contacto-nuevo@turisclick.dev"
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var body = await updateResponse.Content.ReadFromJsonAsync<CompanyResponse>(JsonOptions);
        Assert.Equal("Andes Travel Renovada", body!.Name);
        Assert.Equal("APPROVED", body.Status);
    }

    [Fact]
    public async Task GetMyCompany_AsTourist_Returns403()
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await RegisterAndLoginTouristAsync(client, "mycompany-tourist"));

        var response = await client.GetAsync("/api/companies/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMyCompany_OneProviderCannotSeeAnothers_BecauseScopeComesFromOwnToken()
    {
        // No hay endpoint para pedir la empresa de otro por id — "me" siempre resuelve a la propia
        // empresa del token, así que no existe una forma de "ver la empresa de otro" a través de este endpoint.
        var client = _factory.CreateClient();
        var providerA = await RegisterProviderAsync(client, "mycompany-a");
        var providerB = await RegisterProviderAsync(client, "mycompany-b");

        UseBearerToken(client, providerA.AccessToken);
        var response = await client.GetAsync("/api/companies/me");
        var body = await response.Content.ReadFromJsonAsync<CompanyResponse>(JsonOptions);

        Assert.Equal(providerA.Company.Id, body!.Id);
        Assert.NotEqual(providerB.Company.Id, body.Id);
    }
}
