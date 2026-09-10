using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Reservations.Dtos;

/// <summary>UC-P-14 — el proveedor cancela por fuerza mayor, así que el motivo es obligatorio y queda registrado en la línea.</summary>
public class CancelReservationItemRequest
{
    [Required, MinLength(5), MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}
