namespace TurisClick.Api.Modules.Reservations.Entities;

public enum ReservationItemStatus
{
    PENDING_PAYMENT,
    CONFIRMED,
    /// <summary>
    /// Cancelado explícitamente: por el turista (UC-T-11, cancela la reserva completa) o por el
    /// proveedor sobre su propia línea (UC-P-14). Distinto de EXPIRED: hubo una decisión humana.
    /// </summary>
    CANCELLED,
    /// <summary>
    /// El hold venció sin pago (UC-SYS-08). Se distingue de CANCELLED a propósito: son eventos de
    /// dominio distintos y conviene poder auditarlos por separado.
    /// </summary>
    EXPIRED
}
