using TurisClick.Api.Modules.Users;

namespace TurisClick.Api.Modules.Providers;

public class Provider
{
    public int ProviderId { get; set; }
    public int UserId { get; set; }

    public string CompanyName { get; set; } = string.Empty;
    public string? Nit { get; set; }
    public string? Description { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public string? Address { get; set; }
    public string? LogoUrl { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}
