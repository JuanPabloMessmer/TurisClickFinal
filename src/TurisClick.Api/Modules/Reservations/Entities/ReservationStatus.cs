namespace TurisClick.Api.Modules.Reservations.Entities;

public enum ReservationStatus
{
    PENDING_PAYMENT,
    CONFIRMED,
    PAYMENT_FAILED,

    /// <summary>
    /// Cancelación en curso: la persona aceptó el presupuesto y hay una llamada externa de por medio
    /// (cancelar el pasaje en la aerolínea). Existe para que una caída a mitad de camino deje un estado
    /// visible en vez de una reserva que parece vigente con un pasaje ya cancelado. Es transitorio: lo
    /// resuelve la misma operación o el proceso de reconciliación.
    /// </summary>
    CANCELLING,

    CANCELLED,
    EXPIRED
}
