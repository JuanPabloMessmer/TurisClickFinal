using Microsoft.EntityFrameworkCore;
using Npgsql;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Packages.Dtos;
using TurisClick.Api.Modules.Packages.Entities;
using TurisClick.Api.Modules.Packages.Repositories;
using TurisClick.Api.Shared.Exceptions;
using TurisClick.Api.Shared.Scheduling;

namespace TurisClick.Api.Modules.Packages.Services;

public class PackageAvailabilityService(
    IPackageAvailabilityRepository availabilityRepository,
    IPackageRepository packageRepository,
    ICompanyOwnershipGuard ownershipGuard,
    TurisClickDbContext db) : IPackageAvailabilityService
{
    private const string PostgresUniqueViolation = "23505";

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

    public async Task<BulkPackageAvailabilityResponse> BulkCreateAsync(
        Guid packageId, BulkCreatePackageAvailabilityRequest request, CancellationToken ct)
    {
        var package = await packageRepository.GetByIdAsync(packageId, ct)
            ?? throw new NotFoundAppException("Paquete no encontrado.");

        ownershipGuard.EnsureOwns(package.CompanyId);

        AvailabilitySchedule.EnsureValidRange(request.StartDate, request.EndDate, DateOnly.FromDateTime(DateTime.UtcNow));
        var weekdays = AvailabilitySchedule.ResolveWeekdays(request.Preset, request.Weekdays);
        var dates = AvailabilitySchedule.ExpandDates(request.StartDate, request.EndDate, weekdays);
        AvailabilitySchedule.EnsureWithinLimit(dates.Count);

        var existing = (await availabilityRepository.ListInRangeAsync(packageId, request.StartDate, request.EndDate, ct))
            .Select(a => a.DepartureDate)
            .ToHashSet();

        var toCreate = new List<PackageAvailability>();
        var skipped = new List<SkippedDepartureResponse>();

        foreach (var date in dates)
        {
            if (existing.Contains(date))
            {
                skipped.Add(new SkippedDepartureResponse { DepartureDate = date, Reason = "ALREADY_EXISTS" });
                continue;
            }

            toCreate.Add(new PackageAvailability
            {
                Id = Guid.NewGuid(),
                PackageId = packageId,
                DepartureDate = date,
                TotalSlots = request.TotalSlots,
                ReservedSlots = 0,
                Status = AvailabilitySlotStatus.OPEN
            });
        }

        if (!request.DryRun && toCreate.Count > 0)
        {
            await availabilityRepository.AddRangeAsync(toCreate, ct);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresUniqueViolation })
            {
                throw new ConflictAppException("Algunas de esas salidas se crearon al mismo tiempo desde otra sesión. No se guardó ningún cambio; volvé a intentarlo.");
            }
        }

        return new BulkPackageAvailabilityResponse
        {
            RequestedCount = dates.Count,
            CreatedCount = toCreate.Count,
            SkippedCount = skipped.Count,
            DryRun = request.DryRun,
            Created = toCreate.Select(ToResponse).ToList(),
            Skipped = skipped
        };
    }

    public async Task<PackageAvailabilityResponse> UpdateAsync(
        Guid packageId, Guid availabilityId, UpdateAvailabilityRequest request, CancellationToken ct)
    {
        var package = await packageRepository.GetByIdAsync(packageId, ct)
            ?? throw new NotFoundAppException("Paquete no encontrado.");

        ownershipGuard.EnsureOwns(package.CompanyId);

        var availability = await availabilityRepository.GetByIdAsync(availabilityId, ct);
        if (availability is null || availability.PackageId != packageId)
            throw new NotFoundAppException("Salida no encontrada.");

        AvailabilitySlotStatus? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            status = Enum.TryParse<AvailabilitySlotStatus>(request.Status.Trim(), ignoreCase: true, out var parsed)
                ? parsed
                : throw new ValidationAppException("Estado inválido: usá OPEN o CLOSED.");
        }

        if (status == AvailabilitySlotStatus.OPEN && availability.DepartureDate < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ValidationAppException("No se puede reabrir una salida pasada.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        if (request.TotalSlots is { } totalSlots)
        {
            var affected = await db.PackageAvailabilities
                .Where(a => a.Id == availabilityId && a.ReservedSlots <= totalSlots)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.TotalSlots, totalSlots), ct);

            if (affected == 0)
                throw new ConflictAppException("El cupo no puede ser menor que los lugares ya reservados.", ErrorCodes.InsufficientCapacity);
        }

        if (status is { } newStatus)
        {
            await db.PackageAvailabilities
                .Where(a => a.Id == availabilityId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, newStatus), ct);
        }

        await transaction.CommitAsync(ct);

        var updated = (await availabilityRepository.ListInRangeAsync(packageId, availability.DepartureDate, availability.DepartureDate, ct))
            .First(a => a.Id == availabilityId);
        return ToResponse(updated);
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
