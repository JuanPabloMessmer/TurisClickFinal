using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Destinations.Dtos;

/// <summary>
/// UC-A-04 — Gestionar destinos (edición). Solo permite renombrar: Type y ParentId son estructurales
/// y no se admiten en la actualización — re-parentar u cambiar de nivel un destino que ya podría tener
/// hijos o (más adelante) productos asociados requeriría revalidar toda la jerarquía por debajo; si el
/// alta fue incorrecta, se borra y se vuelve a crear (la Regla de "sin hijos" en Delete lo protege).
/// </summary>
public class UpdateDestinationRequest
{
    [Required, MinLength(2), MaxLength(150)]
    public string Name { get; set; } = string.Empty;
}
