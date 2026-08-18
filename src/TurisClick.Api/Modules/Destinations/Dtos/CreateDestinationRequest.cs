using System.ComponentModel.DataAnnotations;
using TurisClick.Api.Modules.Destinations.Entities;

namespace TurisClick.Api.Modules.Destinations.Dtos;

/// <summary>UC-A-04 — Gestionar destinos (alta).</summary>
public class CreateDestinationRequest
{
    [Required, MinLength(2), MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    /// <summary>"COUNTRY" | "REGION" | "CITY".</summary>
    [Required, EnumDataType(typeof(DestinationType))]
    public string Type { get; set; } = string.Empty;

    /// <summary>Requerido salvo para Type = COUNTRY.</summary>
    public Guid? ParentId { get; set; }
}
