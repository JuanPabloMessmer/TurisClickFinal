using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;

namespace TurisClick.Api.Modules.Destinations.Repositories;

public class DestinationRepository : IDestinationRepository
{
    private readonly AppDbContext _dbContext;

    public DestinationRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Destination>> GetAllAsync()
    {
        return await _dbContext.Destinations
            .AsNoTracking()
            .OrderBy(destination => destination.Name)
            .ThenBy(destination => destination.Department)
            .ToListAsync();
    }

    public async Task<Destination?> GetByIdAsync(int destinationId)
    {
        return await _dbContext.Destinations
            .FirstOrDefaultAsync(destination => destination.DestinationId == destinationId);
    }

    public async Task<bool> NameAndDepartmentExistsAsync(
        string name,
        string? department,
        int? excludedDestinationId = null)
    {
        return await _dbContext.Destinations.AnyAsync(destination =>
            destination.Name == name &&
            destination.Department == department &&
            (!excludedDestinationId.HasValue || destination.DestinationId != excludedDestinationId.Value));
    }

    public async Task<Destination> CreateAsync(Destination destination)
    {
        _dbContext.Destinations.Add(destination);
        await _dbContext.SaveChangesAsync();

        return destination;
    }

    public async Task SaveChangesAsync()
    {
        await _dbContext.SaveChangesAsync();
    }
}
