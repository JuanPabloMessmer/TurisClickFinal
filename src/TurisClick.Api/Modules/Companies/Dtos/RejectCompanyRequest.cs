using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Companies.Dtos;

/// <summary>UC-A-03 — Rechazar solicitud de empresa.</summary>
public class RejectCompanyRequest
{
    [Required, MinLength(5), MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}
