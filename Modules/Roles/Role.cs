namespace TurisClick.Api.Modules.Roles;

public class Role
{
    public int IdRol { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }

    public ICollection<Users.User> Usuarios { get; set; } = new List<Users.User>();
}