using TurisClick.Api.Modules.Reservations.Entities;

namespace TurisClick.Api.Modules.Reservations.Repositories;

/// <summary>Consultas a nivel de ReservationItem para el PROVIDER (UC-P-12/13) — un Provider nunca ve Items de otra empresa.</summary>
public interface IReservationItemRepository
{
    Task<(List<ReservationItem> Items, int TotalCount)> ListByCompanyAsync(
        Guid companyId, int page, int pageSize, CancellationToken ct);

    /// <summary>Sin filtrar por empresa — el Service decide 403 vs 200 tras cargarlo (UC-SYS-03).</summary>
    Task<ReservationItem?> GetByIdAsync(Guid id, CancellationToken ct);
}
