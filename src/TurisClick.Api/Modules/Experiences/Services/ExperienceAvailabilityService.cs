using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Experiences.Repositories;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Experiences.Services;

public class ExperienceAvailabilityService(
    IExperienceAvailabilityRepository availabilityRepository,
    IExperienceRepository experienceRepository,
    ICompanyOwnershipGuard ownershipGuard,
    TurisClickDbContext db) : IExperienceAvailabilityService
{
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
