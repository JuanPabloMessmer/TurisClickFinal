using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Infrastructure.Security;

/// <summary>Quién es el usuario autenticado del request actual, leído de los claims del JWT ya validado.</summary>
public interface ICurrentUserContext
{
    bool IsAuthenticated { get; }
    Guid UserId { get; }
    string Role { get; }

    /// <summary>Nulo si el usuario autenticado no es PROVIDER.</summary>
    Guid? CompanyId { get; }
}

public class CurrentUserContext(IHttpContextAccessor httpContextAccessor) : ICurrentUserContext
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid UserId =>
        Guid.TryParse(Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)
            ? id
            : throw new UnauthorizedAppException("No hay un usuario autenticado en el contexto actual.");

    public string Role =>
        Principal?.FindFirstValue(ClaimTypes.Role)
        ?? throw new UnauthorizedAppException("No hay un usuario autenticado en el contexto actual.");

    public Guid? CompanyId =>
        Guid.TryParse(Principal?.FindFirstValue("company_id"), out var companyId) ? companyId : null;
}
