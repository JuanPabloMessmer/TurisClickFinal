using System.ComponentModel.DataAnnotations;
using TurisClick.Api.Shared.Validation;

namespace TurisClick.Api.Modules.Companies.Dtos;

/// <summary>UC-A-03 — Rechazar solicitud de empresa. Regla R-10 de docs/auditoria-validaciones.md.</summary>
public class RejectCompanyRequest
{
    [Required(AllowEmptyStrings = true)] // obligatorio en el contrato OpenAPI; el mensaje lo da la regla propia
    [Rule.PlainText("El motivo", ValidationLimits.RejectionReasonMax, Min = ValidationLimits.RejectionReasonMin,
        Required = true, MultiLine = true)]
    public string Reason { get; set; } = string.Empty;
}
