using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Destinations.Entities;

namespace TurisClick.Api.Modules.Destinations.Services;

/// <summary>UC-T-03 — Explorar destinos. Separado de IDestinationService (admin) por audiencia/autorización distinta.</summary>
public interface IPublicDestinationService
{
    Task<List<PublicDestinationResponse>> ListAsync(Guid? parentId, DestinationType? type, CancellationToken ct);
    Task<PublicDestinationResponse> GetByIdAsync(Guid id, CancellationToken ct);
}
