using TurisClick.Api.Modules.Admin.Dtos;
using TurisClick.Api.Modules.Auth.Entities;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Admin.Services;

/// <summary>
/// UC-A-06 — gestión de cuentas por parte del ADMIN. La suspensión ya se aplica sola en el resto del
/// sistema: <c>AuthService</c> rechaza login y refresh de un usuario SUSPENDED desde Oleada 0.
/// </summary>
public interface IAdminUserService
{
    Task<PagedResult<AdminUserResponse>> ListAsync(
        UserRole? role, UserStatus? status, string? search, int page, int pageSize, CancellationToken ct);

    Task<AdminUserResponse> SuspendAsync(Guid userId, CancellationToken ct);

    /// <summary>Contrapartida de la suspensión: sin esto la sanción sería irreversible.</summary>
    Task<AdminUserResponse> ActivateAsync(Guid userId, CancellationToken ct);
}
