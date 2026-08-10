namespace TurisClick.Api.Modules.Auth.Entities;

/// <summary>docs/domain-model.md §1. CompanyId solo se completa cuando exista el módulo Companies (Oleada 1).</summary>
public class User
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public UserStatus Status { get; set; } = UserStatus.ACTIVE;

    /// <summary>
    /// FK lógica a companies.id. Sin restricción de FK física todavía: la tabla companies no existe
    /// hasta la Oleada 1 (Modules/Companies). Se agrega la FK ahí, no antes (ver database-design.md).
    /// </summary>
    public Guid? CompanyId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
