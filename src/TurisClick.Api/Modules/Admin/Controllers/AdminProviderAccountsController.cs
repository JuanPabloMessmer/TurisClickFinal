using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Admin.Dtos;
using TurisClick.Api.Modules.Admin.Services;

namespace TurisClick.Api.Modules.Admin.Controllers;

/// <summary>
/// Alta de operadores. **En TurisClick una empresa de turismo no se da de alta sola**: la carga alguien de la
/// plataforma que ya la conoce, y recibe sus credenciales por el canal que ya usan.
///
/// Esto reemplaza el autorregistro público que existía antes, y el motivo no es sólo de producto: un endpoint
/// anónimo capaz de crear una cuenta con rol PROVIDER era una escalada de privilegios servida —cualquiera
/// podía obtener permiso de publicar en el catálogo—.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = "RequireAdmin")]
public class AdminProviderAccountsController(IAdminOnboardingService onboarding) : ControllerBase
{
    /// <summary>
    /// Crea la empresa y su primera cuenta de operador. Devuelve la contraseña temporal **una sola vez**: no
    /// se guarda en claro y no se puede volver a consultar.
    /// </summary>
    [HttpPost("provider-accounts")]
    [ProducesResponseType(typeof(ProviderAccountCreatedResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProviderAccountCreatedResponse>> Create(
        [FromBody] CreateProviderAccountRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await onboarding.CreateProviderAccountAsync(request, ct));

    /// <summary>
    /// Regenera la credencial temporal de un operador que perdió la suya. Corta las sesiones abiertas: una
    /// credencial nueva que conviva con la vieja no protege de nada.
    /// </summary>
    [HttpPost("provider-accounts/{userId:guid}/reset-password")]
    [ProducesResponseType(typeof(ResetProviderPasswordResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ResetProviderPasswordResponse>> ResetPassword(Guid userId, CancellationToken ct)
        => Ok(await onboarding.ResetProviderPasswordAsync(userId, ct));

    /// <summary>Las cuentas de una empresa, con si todavía arrastran su contraseña temporal.</summary>
    [HttpGet("companies/{companyId:guid}/users")]
    [ProducesResponseType(typeof(List<CompanyUserResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CompanyUserResponse>>> ListUsers(Guid companyId, CancellationToken ct)
        => Ok(await onboarding.ListCompanyUsersAsync(companyId, ct));
}
