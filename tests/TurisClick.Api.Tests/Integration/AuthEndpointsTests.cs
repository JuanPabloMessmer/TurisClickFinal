using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TurisClick.Api.Modules.Auth.Dtos;
using Xunit;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// Tests de integración end-to-end (HTTP real vía TestServer + Postgres real turisclick_v2_test)
/// de UC-AUTH-01..04. Cada test usa un email único para no colisionar con `uq_users_email`.
/// </summary>
[Collection(ApiCollection.Name)]
public class AuthEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;

    public AuthEndpointsTests(TurisClickApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private static string UniqueEmail([CallerMemberName] string? testName = null) =>
        $"{testName?.ToLowerInvariant()}-{Guid.NewGuid():N}@turisclick.dev";

    [Fact]
    public async Task Register_WithNewEmail_Returns201WithTokens()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Test", lastName = "Tourist",
            email = UniqueEmail(),
            password = "Password123!"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AuthResultResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(body.RefreshToken));
        Assert.Equal("TOURIST", body.User.Role);
        Assert.Equal("Test", body.User.FirstName);
        Assert.Equal("Tourist", body.User.LastName);
        Assert.Equal("Test Tourist", body.User.FullName);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_Returns409()
    {
        var payload = new { firstName = "Test", lastName = "Tourist", email = UniqueEmail(), password = "Password123!" };

        await _client.PostAsJsonAsync("/api/auth/register", payload);
        var second = await _client.PostAsJsonAsync("/api/auth/register", payload);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Register_WithInvalidPayload_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new { firstName = "", lastName = "", email = "no-es-un-email", password = "123" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithValidCredentials_Returns200WithTokens()
    {
        var email = UniqueEmail();
        await _client.PostAsJsonAsync("/api/auth/register", new { firstName = "Test", lastName = "Tourist", email, password = "Password123!" });

        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AuthResultResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(email, body!.User.Email);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var email = UniqueEmail();
        await _client.PostAsJsonAsync("/api/auth/register", new { firstName = "Test", lastName = "Tourist", email, password = "Password123!" });

        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "OtraPassword!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = UniqueEmail(), password = "Password123!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithValidToken_Returns200WithNewTokens()
    {
        var email = UniqueEmail();
        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new { firstName = "Test", lastName = "Tourist", email, password = "Password123!" });
        var registerBody = await registerResponse.Content.ReadFromJsonAsync<AuthResultResponse>(JsonOptions);

        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = registerBody!.RefreshToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var refreshed = await response.Content.ReadFromJsonAsync<AuthResultResponse>(JsonOptions);
        Assert.NotNull(refreshed);
        Assert.NotEqual(registerBody.RefreshToken, refreshed!.RefreshToken); // rotación
    }

    [Fact]
    public async Task Refresh_WithGarbageToken_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "esto-no-existe" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_AfterBeingUsedOnce_Returns401_DueToRotation()
    {
        var email = UniqueEmail();
        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new { firstName = "Test", lastName = "Tourist", email, password = "Password123!" });
        var registerBody = await registerResponse.Content.ReadFromJsonAsync<AuthResultResponse>(JsonOptions);

        await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = registerBody!.RefreshToken });
        var secondUse = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = registerBody.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, secondUse.StatusCode);
    }

    [Fact]
    public async Task Logout_WithoutAccessToken_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = "cualquier-cosa" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_WithValidAccessToken_Returns204AndRevokesRefreshToken()
    {
        var email = UniqueEmail();
        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new { firstName = "Test", lastName = "Tourist", email, password = "Password123!" });
        var registerBody = await registerResponse.Content.ReadFromJsonAsync<AuthResultResponse>(JsonOptions);

        var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = JsonContent.Create(new { refreshToken = registerBody!.RefreshToken })
        };
        logoutRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", registerBody.AccessToken);

        var logoutResponse = await _client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var refreshAfterLogout = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = registerBody.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterLogout.StatusCode);
    }
}
