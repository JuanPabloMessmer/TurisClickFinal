namespace TurisClick.Api.Modules.Destinations.Repositories;

public interface IDestinationRepository
{
    Task<IReadOnlyList<Destination>> GetAllAsync();
    Task<Destination?> GetByIdAsync(int destinationId);
    Task<bool> NameAndDepartmentExistsAsync(string name, string? department, int? excludedDestinationId = null);
    Task<Destination> CreateAsync(Destination destination);
    Task SaveChangesAsync();
}
