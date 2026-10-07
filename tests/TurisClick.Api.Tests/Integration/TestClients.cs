using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TurisClick.Api.Modules.Auth.Dtos;
using TurisClick.Api.Modules.Admin.Dtos;
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

    /// <summary>
    /// Una cuenta de operador lista para operar.
    ///
    /// Desde la Oleada 13 el alta la hace un administrador y la cuenta nace con una contraseña temporal que
    /// **bloquea toda operación** hasta cambiarla, así que este helper recorre el camino completo: alta, login
    /// con la temporal y cambio de contraseña. El token que devuelve ya puede operar.
    /// </summary>
    public record ProviderTestAccount(string AccessToken, CompanySummaryResponse Company, string Email, string Password);

    public static async Task<ProviderTestAccount> RegisterProviderAsync(HttpClient client, string emailPrefix) =>
        await CreateProviderAccountAsync(client, approve: false, emailPrefix);

    /// <summary>
    /// Da de alta una empresa con su cuenta de operador. `approve` decide si la empresa queda aprobada de
    /// entrada (lo que necesitan los tests de catálogo) o pendiente (lo que necesitan los de aprobación).
    /// </summary>
    public static async Task<ProviderTestAccount> CreateProviderAccountAsync(
        HttpClient providerClient, bool approve, string emailPrefix, HttpClient? adminClient = null)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"{emailPrefix}.{suffix}@turisclick.dev";

        var admin = adminClient ?? providerClient;
        var adminToken = await LoginAsAdminAsync(admin);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/provider-accounts")
        {
            Content = JsonContent.Create(new
            {
                companyName = $"Empresa {suffix}",
                legalDocument = $"DOC-{suffix}",
                contactEmail = $"contacto.{suffix}@turisclick.dev",
                firstName = "Operador",
                lastName = "DePrueba",
                email,
                approve,
            }, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var created = await admin.SendAsync(request);
        created.EnsureSuccessStatusCode();

        var account = await created.Content.ReadFromJsonAsync<ProviderAccountCreatedResponse>(JsonOptions);

        // La contraseña temporal sólo sirve para entrar y cambiarla: el token que sale del login todavía lleva
        // el claim que bloquea todo lo demás.
        var login = await providerClient.PostAsJsonAsync("/api/auth/login",
            new { email, password = account!.TemporaryPassword });
        login.EnsureSuccessStatusCode();
        var temporarySession = await login.Content.ReadFromJsonAsync<AuthResultResponse>(JsonOptions);

        const string password = "OperadorTurisClick2026!";

        using var change = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(
                new { currentPassword = account.TemporaryPassword, newPassword = password }, options: JsonOptions),
        };
        change.Headers.Authorization = new AuthenticationHeaderValue("Bearer", temporarySession!.AccessToken);

        var changed = await providerClient.SendAsync(change);
        changed.EnsureSuccessStatusCode();
        var session = await changed.Content.ReadFromJsonAsync<AuthResultResponse>(JsonOptions);

        return new ProviderTestAccount(session!.AccessToken, account.Company, email, password);
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

    /// <summary>
    /// Da de alta un operador con su empresa ya aprobada — atajo para los tests de catálogo y reservas, que
    /// necesitan una empresa APPROVED para poder publicar.
    /// </summary>
    public static Task<ProviderTestAccount> RegisterApprovedProviderAsync(
        HttpClient providerClient, HttpClient adminClient, string emailPrefix) =>
        CreateProviderAccountAsync(providerClient, approve: true, emailPrefix, adminClient);
}
