namespace TurisClick.Api.Modules.Roles;

public class Role
{
    public int RoleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<Users.User> Users { get; set; } = new List<Users.User>();
}
