using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Experiences.Entities;

namespace TurisClick.Api.Modules.Experiences.Repositories;

public class ExperienceAvailabilityRepository(TurisClickDbContext db) : IExperienceAvailabilityRepository
{
    public Task<ExperienceAvailability?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.ExperienceAvailabilities.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<ExperienceAvailability?> GetByIdWithExperienceAsync(Guid id, CancellationToken ct) =>
        db.ExperienceAvailabilities
            .AsNoTracking()
            .Include(a => a.Experience)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<bool> ExistsAsync(Guid experienceId, DateOnly date, TimeOnly? startTime, CancellationToken ct) =>
        db.ExperienceAvailabilities.AnyAsync(a =>
            a.ExperienceId == experienceId && a.Date == date && a.StartTime == startTime, ct);

    public Task<List<ExperienceAvailability>> ListAllAsync(Guid experienceId, CancellationToken ct) =>
        db.ExperienceAvailabilities
            .AsNoTracking()
            .Where(a => a.ExperienceId == experienceId)
            .OrderBy(a => a.Date).ThenBy(a => a.StartTime)
            .ToListAsync(ct);

    public Task<List<ExperienceAvailability>> ListBookableAsync(Guid experienceId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return db.ExperienceAvailabilities
            .AsNoTracking()
            .Where(a => a.ExperienceId == experienceId
                && a.Status == AvailabilitySlotStatus.OPEN
                && a.Date >= today
                && a.ReservedSlots < a.TotalSlots)
            .OrderBy(a => a.Date).ThenBy(a => a.StartTime)
            .ToListAsync(ct);
    }

    public async Task AddAsync(ExperienceAvailability availability, CancellationToken ct) =>
        await db.ExperienceAvailabilities.AddAsync(availability, ct);
}
