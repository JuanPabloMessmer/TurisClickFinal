using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Admin.Dtos;
using TurisClick.Api.Modules.Admin.Services;

namespace TurisClick.Api.Modules.Admin.Controllers;

/// <summary>
/// Alta de administradores. Sólo un administrador puede crear otro —con su contraseña ya cambiada: la política
/// RequireAdmin rechaza a quien todavía tiene la temporal—, y no existe ningún camino anónimo ni de otro rol.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = "RequireAdmin")]
public class AdminAccountsController(IAdminOnboardingService onboarding) : ControllerBase
{
    /// <summary>
    /// Crea una cuenta ADMIN. Devuelve la contraseña temporal **una sola vez**: no se guarda en claro y no se
    /// puede volver a consultar. Un email ya registrado devuelve 409 y esa cuenta no se modifica.
    /// </summary>
    [HttpPost("admin-accounts")]
    [ProducesResponseType(typeof(AdminAccountCreatedResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminAccountCreatedResponse>> Create(
        [FromBody] CreateAdminAccountRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await onboarding.CreateAdminAccountAsync(request, ct));
}
