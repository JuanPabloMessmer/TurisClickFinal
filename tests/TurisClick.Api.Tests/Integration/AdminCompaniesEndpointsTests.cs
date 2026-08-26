using System.Net;
using System.Net.Http.Json;
using TurisClick.Api.Modules.Companies.Dtos;
using TurisClick.Api.Shared.Responses;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>UC-A-01/02/03 — Revisar, aprobar y rechazar solicitudes de empresa. Exclusivo de ADMIN.</summary>
[Collection(ApiCollection.Name)]
public class AdminCompaniesEndpointsTests
{
    private readonly TurisClickApiFactory _factory;

    public AdminCompaniesEndpointsTests(TurisClickApiFactory factory) => _factory = factory;

    [Fact]
    public async Task List_PendingApproval_IncludesJustRegisteredCompany()
    {
        var client = _factory.CreateClient();
        var provider = await RegisterProviderAsync(client, "admin-list");
        UseBearerToken(client, await LoginAsAdminAsync(client));

        var response = await client.GetAsync("/api/admin/companies?status=PENDING_APPROVAL&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<CompanyResponse>>(JsonOptions);
        Assert.Contains(body!.Items, c => c.Id == provider.Company.Id);
    }

    [Fact]
    public async Task List_SearchByName_ReturnsOnlyMatchingCompany()
    {
        var client = _factory.CreateClient();
        var target = await RegisterProviderAsync(client, "admin-search-name");
        await RegisterProviderAsync(client, "admin-search-other");
        UseBearerToken(client, await LoginAsAdminAsync(client));

        var response = await client.GetAsync($"/api/admin/companies?search={Uri.EscapeDataString(target.Company.Name)}&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<CompanyResponse>>(JsonOptions);
        Assert.Contains(body!.Items, c => c.Id == target.Company.Id);
        Assert.All(body.Items, c => Assert.Contains(target.Company.Name, c.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task List_SearchByLegalDocument_ReturnsMatchingCompanyRegardlessOfCase()
    {
        var client = _factory.CreateClient();
        var provider = await RegisterProviderAsync(client, "admin-search-doc");
        UseBearerToken(client, await LoginAsAdminAsync(client));
        var fullDoc = await client.GetAsync($"/api/admin/companies/{provider.Company.Id}");
        var company = await fullDoc.Content.ReadFromJsonAsync<CompanyResponse>(JsonOptions);

        var response = await client.GetAsync($"/api/admin/companies?search={company!.LegalDocument.ToLowerInvariant()}&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<CompanyResponse>>(JsonOptions);
        Assert.Contains(body!.Items, c => c.Id == provider.Company.Id);
    }

    [Fact]
    public async Task List_SearchWithNoMatches_ReturnsEmpty()
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await LoginAsAdminAsync(client));

        var response = await client.GetAsync($"/api/admin/companies?search=zzz-no-existe-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<CompanyResponse>>(JsonOptions);
        Assert.Empty(body!.Items);
    }

    [Fact]
    public async Task Approve_PendingCompany_Returns200WithApprovedStatus()
    {
        var client = _factory.CreateClient();
        var provider = await RegisterProviderAsync(client, "admin-approve");
        UseBearerToken(client, await LoginAsAdminAsync(client));

        var response = await client.PostAsync($"/api/admin/companies/{provider.Company.Id}/approve", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CompanyResponse>(JsonOptions);
        Assert.Equal("APPROVED", body!.Status);
        Assert.NotNull(body.ApprovedAt);
    }

    [Fact]
    public async Task Approve_AlreadyApprovedCompany_Returns409()
    {
        var client = _factory.CreateClient();
        var provider = await RegisterProviderAsync(client, "admin-approve-twice");
        UseBearerToken(client, await LoginAsAdminAsync(client));
        await client.PostAsync($"/api/admin/companies/{provider.Company.Id}/approve", null);

        var secondApprove = await client.PostAsync($"/api/admin/companies/{provider.Company.Id}/approve", null);

        Assert.Equal(HttpStatusCode.Conflict, secondApprove.StatusCode);
    }

    [Fact]
    public async Task Reject_PendingCompany_Returns200WithReasonAndNoApprovedAt()
    {
        var client = _factory.CreateClient();
        var provider = await RegisterProviderAsync(client, "admin-reject");
        UseBearerToken(client, await LoginAsAdminAsync(client));

        var response = await client.PostAsJsonAsync($"/api/admin/companies/{provider.Company.Id}/reject",
            new { reason = "Documentación legal incompleta" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CompanyResponse>(JsonOptions);
        Assert.Equal("REJECTED", body!.Status);
        Assert.Equal("Documentación legal incompleta", body.RejectionReason);
        Assert.Null(body.ApprovedAt);
    }

    [Fact]
    public async Task Reject_WithoutReason_Returns400()
    {
        var client = _factory.CreateClient();
        var provider = await RegisterProviderAsync(client, "admin-reject-noreason");
        UseBearerToken(client, await LoginAsAdminAsync(client));

        var response = await client.PostAsJsonAsync($"/api/admin/companies/{provider.Company.Id}/reject", new { reason = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Approve_AsProvider_Returns403()
    {
        var client = _factory.CreateClient();
        var target = await RegisterProviderAsync(client, "admin-approve-target");
        var attacker = await RegisterProviderAsync(client, "admin-approve-attacker");
        UseBearerToken(client, attacker.AccessToken);

        var response = await client.PostAsync($"/api/admin/companies/{target.Company.Id}/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_AsTourist_Returns403()
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await RegisterAndLoginTouristAsync(client, "admin-list-tourist"));

        var response = await client.GetAsync("/api/admin/companies");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
