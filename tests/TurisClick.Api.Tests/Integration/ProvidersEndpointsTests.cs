using System.Net;
using System.Net.Http.Json;
using TurisClick.Api.Modules.Companies.Dtos;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>UC-P-01 — Registrar empresa y solicitar cuenta de Provider.</summary>
[Collection(ApiCollection.Name)]
public class ProvidersEndpointsTests
{
    private readonly TurisClickApiFactory _factory;

    public ProvidersEndpointsTests(TurisClickApiFactory factory) => _factory = factory;

    private static object ValidPayload(string suffix) => new
    {
        firstName = "Andrés",
        lastName = "Rojas",
        email = $"provider.{suffix}@turisclick.dev",
        password = "Password123!",
        companyName = $"Andes Travel {suffix}",
        companyDescription = "Tours por el Salar de Uyuni",
        legalDocument = $"NIT-{suffix}",
        contactEmail = $"contacto.{suffix}@turisclick.dev"
    };

    [Fact]
    public async Task Register_NewEmailAndDocument_Returns201WithPendingCompany()
    {
        var client = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");

        var response = await client.PostAsJsonAsync("/api/providers/register", ValidPayload(suffix));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RegisterProviderResponse>(JsonOptions);
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));
        Assert.Equal("PROVIDER", body.User.Role);
        Assert.NotNull(body.User.CompanyId);
        Assert.Equal("PENDING_APPROVAL", body.Company.Status);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        var client = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        await client.PostAsJsonAsync("/api/providers/register", ValidPayload(suffix));

        var secondSuffix = Guid.NewGuid().ToString("N");
        var payload = ValidPayload(secondSuffix);
        var withReusedEmail = new
        {
            firstName = "Otro",
            lastName = "Proveedor",
            email = $"provider.{suffix}@turisclick.dev", // mismo email que el primero
            password = "Password123!",
            companyName = $"Otra Empresa {secondSuffix}",
            legalDocument = $"NIT-{secondSuffix}",
            contactEmail = $"contacto.{secondSuffix}@turisclick.dev"
        };

        var response = await client.PostAsJsonAsync("/api/providers/register", withReusedEmail);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_DuplicateLegalDocument_Returns409()
    {
        var client = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        await client.PostAsJsonAsync("/api/providers/register", ValidPayload(suffix));

        var secondSuffix = Guid.NewGuid().ToString("N");
        var withReusedDocument = new
        {
            firstName = "Otro",
            lastName = "Proveedor",
            email = $"provider.{secondSuffix}@turisclick.dev",
            password = "Password123!",
            companyName = $"Otra Empresa {secondSuffix}",
            legalDocument = $"NIT-{suffix}", // mismo documento legal que el primero
            contactEmail = $"contacto.{secondSuffix}@turisclick.dev"
        };

        var response = await client.PostAsJsonAsync("/api/providers/register", withReusedDocument);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_LoginAfterwards_SucceedsEvenWhilePending()
    {
        // Regla de negocio: un Provider con empresa PENDING_APPROVAL puede autenticarse
        // (acceso restringido), no está bloqueado por el estado de la empresa.
        var client = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        await client.PostAsJsonAsync("/api/providers/register", ValidPayload(suffix));

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = $"provider.{suffix}@turisclick.dev",
            password = "Password123!"
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }
}
