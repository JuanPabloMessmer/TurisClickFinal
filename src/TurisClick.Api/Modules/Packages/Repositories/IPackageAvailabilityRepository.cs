using TurisClick.Api.Modules.Packages.Entities;

namespace TurisClick.Api.Modules.Packages.Repositories;

public interface IPackageAvailabilityRepository
{
    Task<PackageAvailability?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>AsNoTracking, con Package cargado — usado por UC-T-09 para validar en una sola consulta.</summary>
    Task<PackageAvailability?> GetByIdWithPackageAsync(Guid id, CancellationToken ct);

    Task<bool> ExistsAsync(Guid packageId, DateOnly departureDate, CancellationToken ct);

    /// <summary>Vista de gestión del PROVIDER dueño — todos los slots, cualquier fecha/estado.</summary>
    Task<List<PackageAvailability>> ListAllAsync(Guid packageId, CancellationToken ct);

    /// <summary>Vista pública para reservar (UC-T-09) — solo OPEN, futuros y con cupo.</summary>
    Task<List<PackageAvailability>> ListBookableAsync(Guid packageId, CancellationToken ct);

    /// <summary>Salidas del paquete dentro del rango (ambos extremos incluidos) — duplicados de un alta masiva en una sola consulta.</summary>
    Task<List<PackageAvailability>> ListInRangeAsync(Guid packageId, DateOnly startDate, DateOnly endDate, CancellationToken ct);

    Task AddAsync(PackageAvailability availability, CancellationToken ct);

    Task AddRangeAsync(IEnumerable<PackageAvailability> availabilities, CancellationToken ct);
}
