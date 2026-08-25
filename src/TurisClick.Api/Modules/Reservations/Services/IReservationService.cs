using TurisClick.Api.Modules.Reservations.Dtos;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Reservations.Services;

public interface IReservationService
{
    /// <summary>UC-T-08 — reserva directa de una Experience individual.</summary>
    Task<ReservationResponse> CreateAsync(CreateReservationRequest request, CancellationToken ct);

    /// <summary>UC-T-10 — detalle de una reserva propia del TOURIST autenticado (403 si no es dueño).</summary>
    Task<ReservationResponse> GetByIdForTouristAsync(Guid id, CancellationToken ct);

    /// <summary>UC-T-10 — "Mis reservas" del TOURIST autenticado.</summary>
    Task<PagedResult<ReservationResponse>> ListMineAsync(int page, int pageSize, CancellationToken ct);

    /// <summary>UC-P-12 — reservas recibidas por la empresa del PROVIDER autenticado, a nivel de ReservationItem.</summary>
    Task<PagedResult<ReservationItemResponse>> ListReceivedByCompanyAsync(int page, int pageSize, CancellationToken ct);

    /// <summary>UC-P-13 — detalle de un ReservationItem recibido (403 si no pertenece a la empresa del PROVIDER).</summary>
    Task<ReservationItemResponse> GetReceivedItemByIdAsync(Guid itemId, CancellationToken ct);

    /// <summary>UC-T-19/UC-SYS-02/UC-SYS-07 — pagar una reserva propia en PENDING_PAYMENT (403 si no es dueño).</summary>
    Task<ReservationResponse> PayAsync(Guid id, PayReservationRequest request, CancellationToken ct);
}
