using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Flights.ProviderFlights.Dtos;
using TurisClick.Api.Modules.Flights.ProviderFlights.Entities;
using TurisClick.Api.Shared.Exceptions;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Flights.ProviderFlights.Services;

public interface IAdminProviderFlightService
{
    Task<FlightSellerAuthorizationResponse> SetFlightSellerAsync(Guid companyId, FlightSellerAuthorizationRequest request, CancellationToken ct);
    Task<PagedResult<ProviderFlightResponse>> ListAsync(AdminProviderFlightsQuery query, CancellationToken ct);
    Task<ProviderFlightResponse> SetSuspendedAsync(Guid flightId, bool suspended, CancellationToken ct);
}

/// <summary>
/// Lo que decide un ADMIN sobre los vuelos de operador: qué empresa puede venderlos y qué vuelo sale de la
/// búsqueda. Ninguna de las dos decisiones toca las reservas ya hechas, que siguen su curso y se emiten igual.
/// </summary>
public class AdminProviderFlightService(
    TurisClickDbContext db,
    ICurrentUserContext currentUser,
    ILogger<AdminProviderFlightService> logger) : IAdminProviderFlightService
{
    public async Task<FlightSellerAuthorizationResponse> SetFlightSellerAsync(
        Guid companyId, FlightSellerAuthorizationRequest request, CancellationToken ct)
    {
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct)
            ?? throw new NotFoundAppException("Empresa no encontrada.");

        if (request.Authorized)
        {
            company.CanSellFlights = true;
            company.FlightSellerRegistration = request.Registration!.Trim();
            company.FlightSellerAuthorizedAt = DateTimeOffset.UtcNow;
            company.FlightSellerAuthorizedByUserId = currentUser.UserId;
        }
        else
        {
            // El registro se conserva: es la constancia de por qué se la había autorizado.
            company.CanSellFlights = false;
            company.FlightSellerAuthorizedAt = null;
            company.FlightSellerAuthorizedByUserId = currentUser.UserId;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Venta de vuelos {Decision} para la empresa {CompanyId}.",
            request.Authorized ? "autorizada" : "revocada", companyId);

        return new FlightSellerAuthorizationResponse
        {
            CompanyId = company.Id,
            CanSellFlights = company.CanSellFlights,
            Registration = company.FlightSellerRegistration,
            AuthorizedAt = company.FlightSellerAuthorizedAt,
            AuthorizedBy = company.FlightSellerAuthorizedByUserId,
        };
    }

    public async Task<PagedResult<ProviderFlightResponse>> ListAsync(AdminProviderFlightsQuery query, CancellationToken ct)
    {
        var flights = db.ProviderFlights.AsNoTracking();

        if (query.CompanyId is { } companyId) flights = flights.Where(f => f.CompanyId == companyId);
        if (Enum.TryParse<ProviderFlightStatus>(query.Status, out var status)) flights = flights.Where(f => f.Status == status);
        if (query.Suspended is { } suspended) flights = flights.Where(f => (f.SuspendedAt != null) == suspended);

        var total = await flights.CountAsync(ct);

        var page = await flights
            .Include(f => f.Company)
            .Include(f => f.Departures)
            .OrderByDescending(f => f.UpdatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .AsSplitQuery()
            .ToListAsync(ct);

        return new PagedResult<ProviderFlightResponse>
        {
            Items = [.. page.Select(ProviderFlightService.ToResponse)],
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = total,
        };
    }

    public async Task<ProviderFlightResponse> SetSuspendedAsync(Guid flightId, bool suspended, CancellationToken ct)
    {
        var flight = await db.ProviderFlights
            .Include(f => f.Company)
            .Include(f => f.Departures)
            .FirstOrDefaultAsync(f => f.Id == flightId, ct)
            ?? throw new NotFoundAppException("Vuelo no encontrado.");

        flight.SuspendedAt = suspended ? DateTimeOffset.UtcNow : null;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Vuelo de operador {FlightId} {Decision} por un administrador.",
            flightId, suspended ? "suspendido" : "reactivado");

        return ProviderFlightService.ToResponse(flight);
    }
}
