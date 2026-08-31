using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Packages.Dtos;
using TurisClick.Api.Modules.Packages.Entities;
using TurisClick.Api.Modules.Packages.Repositories;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Packages.Services;

public class PackageAvailabilityService(
    IPackageAvailabilityRepository availabilityRepository,
    IPackageRepository packageRepository,
    ICompanyOwnershipGuard ownershipGuard,
    TurisClickDbContext db) : IPackageAvailabilityService
{
    public async Task<PackageAvailabilityResponse> CreateAsync(
        Guid packageId, CreatePackageAvailabilityRequest request, CancellationToken ct)
    {
        var package = await packageRepository.GetByIdAsync(packageId, ct)
            ?? throw new NotFoundAppException("Paquete no encontrado.");

        ownershipGuard.EnsureOwns(package.CompanyId);

        if (request.DepartureDate < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ValidationAppException("No se puede crear disponibilidad para una fecha de salida pasada.");

        if (await availabilityRepository.ExistsAsync(packageId, request.DepartureDate, ct))
            throw new ConflictAppException("Ya existe una salida para esa fecha.");

        var availability = new PackageAvailability
        {
            Id = Guid.NewGuid(),
            PackageId = packageId,
            DepartureDate = request.DepartureDate,
            TotalSlots = request.TotalSlots,
            ReservedSlots = 0,
            Status = AvailabilitySlotStatus.OPEN
        };

        await availabilityRepository.AddAsync(availability, ct);
        await db.SaveChangesAsync(ct);

        return ToResponse(availability);
    }

    public async Task<List<PackageAvailabilityResponse>> ListOwnedAsync(Guid packageId, CancellationToken ct)
    {
        var package = await packageRepository.GetByIdAsync(packageId, ct)
            ?? throw new NotFoundAppException("Paquete no encontrado.");

        ownershipGuard.EnsureOwns(package.CompanyId);

        var availabilities = await availabilityRepository.ListAllAsync(packageId, ct);
        return availabilities.Select(ToResponse).ToList();
    }

    public async Task<List<PackageAvailabilityResponse>> ListPublicAsync(Guid packageId, CancellationToken ct)
    {
        var package = await packageRepository.GetByIdAsync(packageId, ct);

        if (package is null || package.Status != PublicationStatus.PUBLISHED)
            throw new NotFoundAppException("Paquete no encontrado.");

        var availabilities = await availabilityRepository.ListBookableAsync(packageId, ct);
        return availabilities.Select(ToResponse).ToList();
    }

    private static PackageAvailabilityResponse ToResponse(PackageAvailability availability) => new()
    {
        Id = availability.Id,
        PackageId = availability.PackageId,
        DepartureDate = availability.DepartureDate,
        TotalSlots = availability.TotalSlots,
        ReservedSlots = availability.ReservedSlots,
        AvailableSlots = availability.AvailableSlots,
        Status = availability.Status.ToString()
    };
}
