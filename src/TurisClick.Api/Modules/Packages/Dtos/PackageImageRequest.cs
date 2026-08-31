using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Packages.Dtos;

public class PackageImageRequest
{
    [Required, Url, MaxLength(500)]
    public string Url { get; set; } = string.Empty;

    public bool IsCover { get; set; }
}
