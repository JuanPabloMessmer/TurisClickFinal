namespace TurisClick.Api.Modules.Admin.Dtos;

/// <summary>UC-A-06 — vista de administración de una cuenta. No expone hashes ni datos de sesión.</summary>
public class AdminUserResponse
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    /// <summary>true si la cuenta todavía tiene la contraseña temporal con la que se la creó o se la regeneró.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>Presente solo para usuarios PROVIDER.</summary>
    public Guid? CompanyId { get; set; }
    public string? CompanyName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
