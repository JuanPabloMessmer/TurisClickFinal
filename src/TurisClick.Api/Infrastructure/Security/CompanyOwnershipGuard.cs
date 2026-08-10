using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Infrastructure.Security;

/// <summary>
/// UC-SYS-03 — Validar propiedad de una empresa.
/// Solo la infraestructura por ahora: todavía no hay módulos (Companies/Experiences/Packages/Reservations)
/// que la invoquen; su uso completo llega con esas oleadas (ver Modules/Companies/README.md).
/// </summary>
public interface ICompanyOwnershipGuard
{
    /// <summary>Lanza ForbiddenAppException si el usuario actual no es PROVIDER de esa empresa.</summary>
    void EnsureOwns(Guid resourceCompanyId);
}

public class CompanyOwnershipGuard(ICurrentUserContext currentUser) : ICompanyOwnershipGuard
{
    public void EnsureOwns(Guid resourceCompanyId)
    {
        if (currentUser.CompanyId is null || currentUser.CompanyId.Value != resourceCompanyId)
            throw new ForbiddenAppException("No tenés permiso sobre un recurso de otra empresa.");
    }
}
