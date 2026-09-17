using Microsoft.EntityFrameworkCore;
using Npgsql;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Experiences.Repositories;
using TurisClick.Api.Shared.Exceptions;
using TurisClick.Api.Shared.Scheduling;

namespace TurisClick.Api.Modules.Experiences.Services;

public class ExperienceAvailabilityService(
    IExperienceAvailabilityRepository availabilityRepository,
    IExperienceRepository experienceRepository,
    ICompanyOwnershipGuard ownershipGuard,
    TurisClickDbContext db) : IExperienceAvailabilityService
{
    private const string PostgresUniqueViolation = "23505";

    public async Task<ExperienceAvailabilityResponse> CreateAsync(
        Guid experienceId, CreateExperienceAvailabilityRequest request, CancellationToken ct)
    {
        var experience = await experienceRepository.GetByIdAsync(experienceId, ct)
            ?? throw new NotFoundAppException("Experiencia no encontrada.");

        ownershipGuard.EnsureOwns(experience.CompanyId);

        if (request.Date < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ValidationAppException("No se puede crear disponibilidad para una fecha pasada.");

        if (await availabilityRepository.ExistsAsync(experienceId, request.Date, request.StartTime, ct))
            throw new ConflictAppException("Ya existe un slot de disponibilidad para esa fecha/hora.");

        var availability = new ExperienceAvailability
        {
            Id = Guid.NewGuid(),
            ExperienceId = experienceId,
            Date = request.Date,
            StartTime = request.StartTime,
            TotalSlots = request.TotalSlots,
            ReservedSlots = 0,
            Status = AvailabilitySlotStatus.OPEN
        };

        await availabilityRepository.AddAsync(availability, ct);
        await db.SaveChangesAsync(ct);

        return ToResponse(availability);
    }

    public async Task<BulkExperienceAvailabilityResponse> BulkCreateAsync(
        Guid experienceId, BulkCreateExperienceAvailabilityRequest request, CancellationToken ct)
    {
        var experience = await experienceRepository.GetByIdAsync(experienceId, ct)
            ?? throw new NotFoundAppException("Experiencia no encontrada.");

        ownershipGuard.EnsureOwns(experience.CompanyId);

        AvailabilitySchedule.EnsureValidRange(request.StartDate, request.EndDate, DateOnly.FromDateTime(DateTime.UtcNow));
        var weekdays = AvailabilitySchedule.ResolveWeekdays(request.Preset, request.Weekdays);
        var dates = AvailabilitySchedule.ExpandDates(request.StartDate, request.EndDate, weekdays);
        var startTimes = AvailabilitySchedule.NormalizeStartTimes(request.StartTimes);
        AvailabilitySchedule.EnsureWithinLimit(dates.Count * startTimes.Count);

        // Una sola lectura del rango: lo que ya existe se OMITE, nunca se pisa (conserva su cupo y sus reservas).
        var existing = (await availabilityRepository.ListInRangeAsync(experienceId, request.StartDate, request.EndDate, ct))
            .Select(a => (a.Date, a.StartTime))
            .ToHashSet();

        var toCreate = new List<ExperienceAvailability>();
        var skipped = new List<SkippedAvailabilityResponse>();

        foreach (var date in dates)
        {
            foreach (var startTime in startTimes)
            {
                if (existing.Contains((date, startTime)))
                {
                    skipped.Add(new SkippedAvailabilityResponse { Date = date, StartTime = startTime, Reason = "ALREADY_EXISTS" });
                    continue;
                }

                toCreate.Add(new ExperienceAvailability
                {
                    Id = Guid.NewGuid(),
                    ExperienceId = experienceId,
                    Date = date,
                    StartTime = startTime,
                    TotalSlots = request.TotalSlots,
                    ReservedSlots = 0,
                    Status = AvailabilitySlotStatus.OPEN
                });
            }
        }

        if (!request.DryRun && toCreate.Count > 0)
        {
            // Un único SaveChanges = una única transacción: o se crean todas o ninguna.
            await availabilityRepository.AddRangeAsync(toCreate, ct);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresUniqueViolation })
            {
                // Otra operación creó alguna de estas fechas entre la lectura y la escritura: no se creó nada.
                throw new ConflictAppException("Algunas de esas fechas se crearon al mismo tiempo desde otra sesión. No se guardó ningún cambio; volvé a intentarlo.");
            }
        }

        return new BulkExperienceAvailabilityResponse
        {
            RequestedCount = dates.Count * startTimes.Count,
            CreatedCount = toCreate.Count,
            SkippedCount = skipped.Count,
            DryRun = request.DryRun,
            Created = toCreate.Select(ToResponse).ToList(),
            Skipped = skipped
        };
    }

    public async Task<ExperienceAvailabilityResponse> UpdateAsync(
        Guid experienceId, Guid availabilityId, UpdateAvailabilityRequest request, CancellationToken ct)
    {
        var experience = await experienceRepository.GetByIdAsync(experienceId, ct)
            ?? throw new NotFoundAppException("Experiencia no encontrada.");

        ownershipGuard.EnsureOwns(experience.CompanyId);

        var availability = await availabilityRepository.GetByIdAsync(availabilityId, ct);
        if (availability is null || availability.ExperienceId != experienceId)
            throw new NotFoundAppException("Disponibilidad no encontrada.");

        var status = ParseStatus(request.Status);
        if (status == AvailabilitySlotStatus.OPEN && availability.Date < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ValidationAppException("No se puede reabrir una fecha pasada.");

        // Cupo y estado se aplican juntos o no se aplica ninguno.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        if (request.TotalSlots is { } totalSlots)
        {
            // Condicional en la base: si entre la lectura y este UPDATE entró una reserva, la condición
            // vuelve a evaluarse con el valor real y nunca queda un cupo menor que lo reservado.
            var affected = await db.ExperienceAvailabilities
                .Where(a => a.Id == availabilityId && a.ReservedSlots <= totalSlots)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.TotalSlots, totalSlots), ct);

            if (affected == 0)
                throw new ConflictAppException("El cupo no puede ser menor que los lugares ya reservados.", ErrorCodes.InsufficientCapacity);
        }

        if (status is { } newStatus)
        {
            await db.ExperienceAvailabilities
                .Where(a => a.Id == availabilityId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, newStatus), ct);
        }

        await transaction.CommitAsync(ct);

        var updated = (await availabilityRepository.ListInRangeAsync(experienceId, availability.Date, availability.Date, ct))
            .First(a => a.Id == availabilityId);
        return ToResponse(updated);
    }

    private static AvailabilitySlotStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        return Enum.TryParse<AvailabilitySlotStatus>(status.Trim(), ignoreCase: true, out var parsed)
            ? parsed
            : throw new ValidationAppException("Estado inválido: usá OPEN o CLOSED.");
    }

    public async Task<List<ExperienceAvailabilityResponse>> ListOwnedAsync(Guid experienceId, CancellationToken ct)
    {
        var experience = await experienceRepository.GetByIdAsync(experienceId, ct)
            ?? throw new NotFoundAppException("Experiencia no encontrada.");

        ownershipGuard.EnsureOwns(experience.CompanyId);

        var availabilities = await availabilityRepository.ListAllAsync(experienceId, ct);
        return availabilities.Select(ToResponse).ToList();
    }

    public async Task<List<ExperienceAvailabilityResponse>> ListPublicAsync(Guid experienceId, CancellationToken ct)
    {
        var experience = await experienceRepository.GetByIdAsync(experienceId, ct);

        if (experience is null || experience.Status != PublicationStatus.PUBLISHED)
            throw new NotFoundAppException("Experiencia no encontrada.");

        var availabilities = await availabilityRepository.ListBookableAsync(experienceId, ct);
        return availabilities.Select(ToResponse).ToList();
    }

    private static ExperienceAvailabilityResponse ToResponse(ExperienceAvailability availability) => new()
    {
        Id = availability.Id,
        ExperienceId = availability.ExperienceId,
        Date = availability.Date,
        StartTime = availability.StartTime,
        TotalSlots = availability.TotalSlots,
        ReservedSlots = availability.ReservedSlots,
        AvailableSlots = availability.AvailableSlots,
        Status = availability.Status.ToString()
    };
}
