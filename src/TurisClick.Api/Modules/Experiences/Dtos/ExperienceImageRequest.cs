using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Experiences.Dtos;

public class ExperienceImageRequest
{
    [Required, Url, MaxLength(500)]
    public string Url { get; set; } = string.Empty;

    public bool IsCover { get; set; }
}
