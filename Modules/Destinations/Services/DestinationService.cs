using Microsoft.EntityFrameworkCore;
using Npgsql;
using TurisClick.Api.Modules.Destinations.DTOs;
using TurisClick.Api.Modules.Destinations.Repositories;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Destinations.Services;

public class DestinationService : IDestinationService
{
    private static readonly HashSet<string> AllowedStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "ACTIVE", "INACTIVE" };

    private readonly IDestinationRepository _destinationRepository;

    public DestinationService(IDestinationRepository destinationRepository)
    {
        _destinationRepository = destinationRepository;
    }

    public async Task<IReadOnlyList<DestinationResponse>> GetAllAsync()
    {
        var destinations = await _destinationRepository.GetAllAsync();
        return destinations.Select(MapToResponse).ToList();
    }

    public async Task<DestinationResponse> GetByIdAsync(int destinationId)
    {
        var destination = await GetRequiredDestinationAsync(destinationId);
        return MapToResponse(destination);
    }

    public async Task<DestinationResponse> CreateAsync(CreateDestinationRequest request)
    {
        var name = NormalizeName(request.Name);
        var department = NormalizeOptional(request.Department);

        if (await _destinationRepository.NameAndDepartmentExistsAsync(name, department))
        {
            throw new ConflictException("Destination name and department are already registered.");
        }

        var destination = new Destination
        {
            Name = name,
            Department = department,
            Description = NormalizeOptional(request.Description),
            DestinationType = NormalizeOptional(request.DestinationType),
            Status = "ACTIVE",
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await _destinationRepository.CreateAsync(destination);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new ConflictException("Destination name and department are already registered.");
        }

        return MapToResponse(destination);
    }

    public async Task<DestinationResponse> UpdateAsync(int destinationId, UpdateDestinationRequest request)
    {
        var destination = await GetRequiredDestinationAsync(destinationId);
        var name = NormalizeName(request.Name);
        var department = NormalizeOptional(request.Department);

        if (await _destinationRepository.NameAndDepartmentExistsAsync(name, department, destination.DestinationId))
        {
            throw new ConflictException("Destination name and department are already registered.");
        }

        destination.Name = name;
        destination.Department = department;
        destination.Description = NormalizeOptional(request.Description);
        destination.DestinationType = NormalizeOptional(request.DestinationType);

        try
        {
            await _destinationRepository.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new ConflictException("Destination name and department are already registered.");
        }

        return MapToResponse(destination);
    }

    public async Task<DestinationResponse> ChangeStatusAsync(
        int destinationId,
        ChangeDestinationStatusRequest request)
    {
        var destination = await GetRequiredDestinationAsync(destinationId);
        var status = request.Status.Trim().ToUpperInvariant();

        if (!AllowedStatuses.Contains(status))
        {
            throw new ValidationException("Status must be ACTIVE or INACTIVE.");
        }

        destination.Status = status;
        await _destinationRepository.SaveChangesAsync();

        return MapToResponse(destination);
    }

    private async Task<Destination> GetRequiredDestinationAsync(int destinationId)
    {
        return await _destinationRepository.GetByIdAsync(destinationId)
            ?? throw new NotFoundException("Destination was not found.");
    }

    private static DestinationResponse MapToResponse(Destination destination)
    {
        return new DestinationResponse
        {
            DestinationId = destination.DestinationId,
            Name = destination.Name,
            Department = destination.Department,
            Description = destination.Description,
            DestinationType = destination.DestinationType,
            Status = destination.Status,
            CreatedAt = destination.CreatedAt
        };
    }

    private static string NormalizeName(string name)
    {
        return name.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        };
    }
}
