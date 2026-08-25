namespace TurisClick.Api.Modules.Reservations.Entities;

public enum ReservationStatus
{
    PENDING_PAYMENT,
    CONFIRMED,
    PAYMENT_FAILED,
    CANCELLED,
    EXPIRED
}
