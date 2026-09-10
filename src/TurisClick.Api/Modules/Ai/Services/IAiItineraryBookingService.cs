using TurisClick.Api.Modules.Ai.Dtos;

namespace TurisClick.Api.Modules.Ai.Services;

/// <summary>
/// UC-T-18 — acepta una propuesta de IA y la convierte en una reserva real. Orquesta: valida
/// pertenencia y estado, revalida cada componente contra Postgres (UC-SYS-01/02) y delega el hold de
/// cupo y la creación de la reserva en el módulo Reservations (UC-SYS-05/06). No implementa lógica de
/// cupos ni de pago propia.
/// </summary>
public interface IAiItineraryBookingService
{
    Task<BookItineraryResponse> BookAsync(Guid itineraryId, BookItineraryRequest request, CancellationToken ct);
}
