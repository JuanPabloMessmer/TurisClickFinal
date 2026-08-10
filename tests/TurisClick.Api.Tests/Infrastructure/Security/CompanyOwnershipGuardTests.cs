using Moq;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Shared.Exceptions;
using Xunit;

namespace TurisClick.Api.Tests.Infrastructure.Security;

/// <summary>
/// UC-SYS-03 — Validar propiedad de una empresa. Solo se prueba la infraestructura en sí
/// (todavía no hay ningún módulo real que la invoque, ver Modules/Companies/README.md).
/// </summary>
public class CompanyOwnershipGuardTests
{
    [Fact]
    public void EnsureOwns_WhenCurrentUserOwnsTheCompany_DoesNotThrow()
    {
        var companyId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUserContext>();
        currentUser.Setup(c => c.CompanyId).Returns(companyId);

        var guard = new CompanyOwnershipGuard(currentUser.Object);

        var exception = Record.Exception(() => guard.EnsureOwns(companyId));
        Assert.Null(exception);
    }

    [Fact]
    public void EnsureOwns_WhenCurrentUserBelongsToAnotherCompany_ThrowsForbidden()
    {
        var currentUser = new Mock<ICurrentUserContext>();
        currentUser.Setup(c => c.CompanyId).Returns(Guid.NewGuid());

        var guard = new CompanyOwnershipGuard(currentUser.Object);

        Assert.Throws<ForbiddenAppException>(() => guard.EnsureOwns(Guid.NewGuid()));
    }

    [Fact]
    public void EnsureOwns_WhenCurrentUserHasNoCompany_ThrowsForbidden()
    {
        var currentUser = new Mock<ICurrentUserContext>();
        currentUser.Setup(c => c.CompanyId).Returns((Guid?)null);

        var guard = new CompanyOwnershipGuard(currentUser.Object);

        Assert.Throws<ForbiddenAppException>(() => guard.EnsureOwns(Guid.NewGuid()));
    }
}
