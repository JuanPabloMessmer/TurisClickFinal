namespace TurisClick.Api.Modules.Reservations.Dtos;

/// <summary>
/// UC-T-19. `Success` solo existe porque el pago hoy es un placeholder simulado (decisión 4 de
/// use-cases.md): permite ejercitar tanto el camino aprobado como el rechazado sin una pasarela real.
/// Cuando se integre una pasarela de verdad, este campo se reemplaza por credenciales/token de pago y
/// el resultado lo determina la respuesta de esa pasarela, no el cliente.
/// </summary>
public class PayReservationRequest
{
    public bool Success { get; set; }

    /// <summary>
    /// UC-SYS-02 — solo importa si el precio vigente cambió desde que se reservó. En true, el turista
    /// acepta explícitamente el precio actual: se recongela UnitPrice/Subtotal con ese valor antes de
    /// cobrar. Si el precio cambió y esto es false, no se cobra ni se confirma nada — la respuesta
    /// devuelve el precio vigente (RequiresPriceAcceptance=true) para que el cliente lo muestre y
    /// reenvíe el pago con AcceptPriceChanges=true una vez que el turista lo acepte.
    /// </summary>
    public bool AcceptPriceChanges { get; set; }
}
