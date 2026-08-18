using TurisClick.Api.Modules.Companies.Entities;

namespace TurisClick.Api.Modules.Auth.Entities;

/// <summary>docs/domain-model.md §1.</summary>
public class User
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public UserStatus Status { get; set; } = UserStatus.ACTIVE;

    /// <summary>Solo aplica si Role = PROVIDER. FK real a companies.id (Oleada 1, ver CompanyConfiguration).</summary>
    public Guid? CompanyId { get; set; }
    public Company? Company { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    /// <summary>Calculado, no persistido — evita guardar FullName como columna redundante (ver docs/domain-model.md).</summary>
    public string FullName => $"{FirstName} {LastName}".Trim();
}
