using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Admin.Dtos;
using TurisClick.Api.Modules.Admin.Services;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Admin.Controllers;

/// <summary>
/// Visibilidad global de la plataforma. Son endpoints **propios** del administrador y no los del operador con
/// el filtro de empresa quitado: los del operador existen para garantizar que nadie vea lo ajeno, y adaptarlos
/// para que a veces vean todo sería debilitar justamente eso.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = "RequireAdmin")]
public class AdminPlatformController(IAdminPlatformService platform) : ControllerBase
{
    /// <summary>Resumen operativo de la plataforma: qué hay, qué entró y qué está esperando a alguien.</summary>
    [HttpGet("overview")]
    [ProducesResponseType(typeof(AdminOverviewResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminOverviewResponse>> Overview(CancellationToken ct)
        => Ok(await platform.GetOverviewAsync(ct));

    /// <summary>Todas las experiencias de la plataforma, en cualquier estado de publicación.</summary>
    [HttpGet("experiences")]
    public async Task<ActionResult<PagedResult<AdminExperienceRowResponse>>> Experiences(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await platform.ListExperiencesAsync(status, search, page, pageSize, ct));

    /// <summary>Todos los paquetes, con si incluyen vuelo y si tienen política de cancelación.</summary>
    [HttpGet("packages")]
    public async Task<ActionResult<PagedResult<AdminPackageRowResponse>>> Packages(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] bool? withFlight,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await platform.ListPackagesAsync(status, search, withFlight, page, pageSize, ct));

    /// <summary>
    /// Todas las reservas, con el estado del pasaje, de la cancelación y de la plata. `needsAttention=true`
    /// deja sólo lo que espera a alguien, que es lo que convierte el listado en una cola de trabajo.
    ///
    /// No incluye datos de pasajero: no existen en la base y no se agregan por tener un rol global.
    /// </summary>
    [HttpGet("reservations")]
    public async Task<ActionResult<PagedResult<AdminReservationRowResponse>>> Reservations(
        [FromQuery] string? status,
        [FromQuery] bool? needsAttention,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await platform.ListReservationsAsync(status, needsAttention, page, pageSize, ct));
}
