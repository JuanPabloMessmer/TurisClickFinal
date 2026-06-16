using TurisClick.Api.Modules.Roles;

namespace TurisClick.Api.Modules.Users;

public class User
{
    public int IdUsuario { get; set; }
    public int IdRol { get; set; }

    public string Nombre { get; set; } = string.Empty;
    public string? Apellido { get; set; }
    public string Correo { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public string Estado { get; set; } = "ACTIVO";
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;

    public Role Rol { get; set; } = null!;
}