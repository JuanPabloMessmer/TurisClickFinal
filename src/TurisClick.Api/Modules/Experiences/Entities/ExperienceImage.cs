namespace TurisClick.Api.Modules.Experiences.Entities;

public class ExperienceImage
{
    public Guid Id { get; set; }
    public Guid ExperienceId { get; set; }
    public Experience? Experience { get; set; }

    public string Url { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsCover { get; set; }
}
