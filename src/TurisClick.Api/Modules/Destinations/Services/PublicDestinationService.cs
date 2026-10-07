using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Destinations.Entities;
using TurisClick.Api.Modules.Destinations.Repositories;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Destinations.Services;

public class PublicDestinationService(IDestinationRepository destinationRepository, TurisClickDbContext db) : IPublicDestinationService
{
    public async Task<List<PublicDestinationResponse>> ListAsync(Guid? parentId, DestinationType? type, CancellationToken ct)
    {
        var destinations = await destinationRepository.ListAsync(parentId, type, ct);
        var ids = destinations.Select(d => d.Id).ToList();
        var experienceCounts = await GetPublishedExperienceCountsAsync(ids, ct);
        var packageCounts = await GetPublishedPackageCountsAsync(ids, ct);

        return destinations
            .Select(d => ToResponse(d, experienceCounts.GetValueOrDefault(d.Id), packageCounts.GetValueOrDefault(d.Id)))
            .ToList();
    }

    public async Task<PublicDestinationResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var destination = await destinationRepository.GetByIdWithParentAsync(id, ct)
            ?? throw new NotFoundAppException("Destino no encontrado.");

        var experienceCounts = await GetPublishedExperienceCountsAsync([id], ct);
        var packageCounts = await GetPublishedPackageCountsAsync([id], ct);

        return ToResponse(destination, experienceCounts.GetValueOrDefault(id), packageCounts.GetValueOrDefault(id));
    }

    /// <summary>Un solo GROUP BY para todos los destinos pedidos — evita N+1 al listar.</summary>
    private async Task<Dictionary<Guid, int>> GetPublishedExperienceCountsAsync(List<Guid> destinationIds, CancellationToken ct)
    {
        if (destinationIds.Count == 0)
            return [];

        return await db.Experiences
            .Where(e => e.Status == PublicationStatus.PUBLISHED && destinationIds.Contains(e.DestinationId))
            .GroupBy(e => e.DestinationId)
            .Select(g => new { DestinationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DestinationId, x => x.Count, ct);
    }

    /// <summary>Lo mismo para paquetes, en una consulta aparte: son dos tablas distintas, no un join.</summary>
    private async Task<Dictionary<Guid, int>> GetPublishedPackageCountsAsync(List<Guid> destinationIds, CancellationToken ct)
    {
        if (destinationIds.Count == 0)
            return [];

        return await db.Packages
            .Where(p => p.Status == PublicationStatus.PUBLISHED && destinationIds.Contains(p.DestinationId))
            .GroupBy(p => p.DestinationId)
            .Select(g => new { DestinationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DestinationId, x => x.Count, ct);
    }

    private static PublicDestinationResponse ToResponse(
        Destination destination,
        int publishedExperienceCount,
        int publishedPackageCount) => new()
    {
        Id = destination.Id,
        Name = destination.Name,
        Type = destination.Type.ToString(),
        ParentId = destination.ParentId,
        ParentName = destination.Parent?.Name,
        ImageUrl = destination.ImageUrl,
        PublishedExperienceCount = publishedExperienceCount,
        PublishedPackageCount = publishedPackageCount
    };
}
