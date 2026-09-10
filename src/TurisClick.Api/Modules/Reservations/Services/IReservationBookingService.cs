using TurisClick.Api.Modules.Reservations.Entities;

namespace TurisClick.Api.Modules.Reservations.Services;

/// <summary>
/// UC-SYS-05/06 — primitiva compartida de reserva multi-ítem. Vive en el módulo Reservations a
/// propósito: el módulo de IA orquesta (valida el itinerario, resuelve preferencias), pero **no
/// implementa su propio motor de reservas**. Acá está la única lógica que toca cupos.
///
/// No abre ni commitea la transacción: la abre el caller, porque la unidad atómica de UC-T-18 incluye
/// además la transición `AiItinerary → BOOKED`, que pertenece al otro módulo (ver
/// docs/backend-architecture.md, sección de booking desde itinerario IA).
/// </summary>
public interface IReservationBookingService
{
    /// <summary>
    /// Toma los cupos de todas las líneas y arma la <see cref="Reservation"/> con sus ítems, sin
    /// persistir todavía. Lanza <c>ConflictAppException</c> si alguna línea no consigue cupo — como
    /// todo corre dentro de la transacción del caller, un fallo revierte también los holds ya tomados.
    /// </summary>
    Task<Reservation> HoldAndBuildAsync(
        Guid touristId, Guid? aiItineraryId, IReadOnlyList<BookingLine> lines, CancellationToken ct);
}

/// <summary>
/// Una línea a reservar, ya resuelta y validada por el caller **contra la base** (nunca contra datos
/// del request): producto real, availability real, empresa dueña y precio/moneda vigentes.
/// </summary>
public record BookingLine(
    ProductType ProductType,
    Guid ProductId,
    Guid AvailabilityId,
    Guid CompanyId,
    int Travelers,
    decimal UnitPrice,
    string Currency,
    int? DayNumber);
