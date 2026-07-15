namespace TurisClick.Api.Modules.Providers.DTOs;

public class ProviderResponse
{
    public int ProviderId { get; set; }
    public int UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? Nit { get; set; }
    public string? Description { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public string? Address { get; set; }
    public string? LogoUrl { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
