using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TurisClick.Api.Modules.Auth.Dtos;
using TurisClick.Api.Modules.Companies.Dtos;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// Helpers compartidos por los tests de integración de Oleada 1 — evita repetir en cada clase el
/// login como ADMIN (usuario sembrado por TurisClickApiFactory) y el registro de un PROVIDER de prueba.
/// </summary>
internal static class TestClients
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<string> LoginAsAdminAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = TurisClickApiFactory.AdminEmail,
            password = TurisClickApiFactory.AdminPassword
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuthResultResponse>(JsonOptions);
        return body!.AccessToken;
    }

    public static async Task<RegisterProviderResponse> RegisterProviderAsync(HttpClient client, string emailPrefix)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync("/api/providers/register", new
        {
            firstName = "Provider",
            lastName = "DePrueba",
            email = $"{emailPrefix}.{suffix}@turisclick.dev",
            password = "Password123!",
            companyName = $"Empresa {suffix}",
            legalDocument = $"DOC-{suffix}",
            contactEmail = $"contacto.{suffix}@turisclick.dev"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RegisterProviderResponse>(JsonOptions))!;
    }

    public static async Task<string> RegisterAndLoginTouristAsync(HttpClient client, string emailPrefix)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Tourist",
            lastName = "DePrueba",
            email = $"{emailPrefix}.{suffix}@turisclick.dev",
            password = "Password123!"
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuthResultResponse>(JsonOptions);
        return body!.AccessToken;
    }

    public static void UseBearerToken(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    /// <summary>Registra un Provider y aprueba su empresa de inmediato (usa un HttpClient de ADMIN aparte) — atajo para tests de Experiences/Reservations que requieren Company APPROVED.</summary>
    public static async Task<RegisterProviderResponse> RegisterApprovedProviderAsync(
        HttpClient providerClient, HttpClient adminClient, string emailPrefix)
    {
        var provider = await RegisterProviderAsync(providerClient, emailPrefix);

        UseBearerToken(adminClient, await LoginAsAdminAsync(adminClient));
        var approveResponse = await adminClient.PostAsync($"/api/admin/companies/{provider.Company.Id}/approve", null);
        approveResponse.EnsureSuccessStatusCode();

        return provider;
    }
}
