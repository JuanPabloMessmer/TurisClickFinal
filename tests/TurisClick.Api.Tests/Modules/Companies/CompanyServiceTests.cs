using Microsoft.EntityFrameworkCore;
using Moq;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Auth.Dtos;
using TurisClick.Api.Modules.Auth.Entities;
using TurisClick.Api.Modules.Auth.Repositories;
using TurisClick.Api.Modules.Auth.Services;
using TurisClick.Api.Modules.Companies.Dtos;
using TurisClick.Api.Modules.Companies.Entities;
using TurisClick.Api.Modules.Companies.Repositories;
using TurisClick.Api.Modules.Companies.Services;
using TurisClick.Api.Shared.Exceptions;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Companies;

/// <summary>UC-P-01, UC-A-01/02/03, UC-P-02.</summary>
public class CompanyServiceTests
{
    private readonly Mock<ICompanyRepository> _companyRepository = new();
    private readonly CompanyService _sut;

    public CompanyServiceTests()
    {
        var options = new DbContextOptionsBuilder<TurisClickDbContext>().Options;
        var db = new Mock<TurisClickDbContext>(options);
        db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _sut = new CompanyService(_companyRepository.Object, db.Object);
    }

    // Las tres pruebas del autorregistro de operadores se movieron: ese camino se eliminó en la Oleada 13
    // (una empresa no se da de alta sola) y las mismas garantías —email duplicado, documento legal duplicado y
    // la cuenta que nace con rol PROVIDER ligada a su empresa— se ejercitan ahora de punta a punta en
    // AdminPlatformTests, contra el endpoint que las reemplaza.

    [Fact]
    public async Task ApproveAsync_FromPendingApproval_SetsApprovedFields()
    {
        var companyId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var company = new Company { Id = companyId, Status = CompanyStatus.PENDING_APPROVAL };
        _companyRepository.Setup(r => r.GetByIdAsync(companyId, It.IsAny<CancellationToken>())).ReturnsAsync(company);

        var result = await _sut.ApproveAsync(companyId, adminId, CancellationToken.None);

        Assert.Equal("APPROVED", result.Status);
        Assert.NotNull(result.ApprovedAt);
        Assert.Equal(adminId, company.ApprovedByUserId);
    }

    [Theory]
    [InlineData(CompanyStatus.APPROVED)]
    [InlineData(CompanyStatus.REJECTED)]
    [InlineData(CompanyStatus.SUSPENDED)]
    public async Task ApproveAsync_WhenNotPendingApproval_ThrowsConflict(CompanyStatus currentStatus)
    {
        var companyId = Guid.NewGuid();
        _companyRepository.Setup(r => r.GetByIdAsync(companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Company { Id = companyId, Status = currentStatus });

        await Assert.ThrowsAsync<ConflictAppException>(() => _sut.ApproveAsync(companyId, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task RejectAsync_FromPendingApproval_SetsReasonButLeavesApprovedAtNull()
    {
        var companyId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var company = new Company { Id = companyId, Status = CompanyStatus.PENDING_APPROVAL };
        _companyRepository.Setup(r => r.GetByIdAsync(companyId, It.IsAny<CancellationToken>())).ReturnsAsync(company);

        var result = await _sut.RejectAsync(companyId, adminId, new RejectCompanyRequest { Reason = "Documentación incompleta" }, CancellationToken.None);

        Assert.Equal("REJECTED", result.Status);
        Assert.Equal("Documentación incompleta", result.RejectionReason);
        Assert.Null(result.ApprovedAt);
        Assert.Equal(adminId, company.ApprovedByUserId);
    }

    [Fact]
    public async Task RejectAsync_WhenNotPendingApproval_ThrowsConflict()
    {
        var companyId = Guid.NewGuid();
        _companyRepository.Setup(r => r.GetByIdAsync(companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Company { Id = companyId, Status = CompanyStatus.APPROVED });

        await Assert.ThrowsAsync<ConflictAppException>(() =>
            _sut.RejectAsync(companyId, Guid.NewGuid(), new RejectCompanyRequest { Reason = "motivo" }, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateMyCompanyAsync_WhenApproved_UpdatesFields()
    {
        var companyId = Guid.NewGuid();
        var company = new Company { Id = companyId, Status = CompanyStatus.APPROVED, Name = "Viejo nombre", ContactEmail = "old@x.dev" };
        _companyRepository.Setup(r => r.GetByIdAsync(companyId, It.IsAny<CancellationToken>())).ReturnsAsync(company);

        var request = new UpdateCompanyRequest { Name = "Nuevo nombre", ContactEmail = "nuevo@x.dev" };
        var result = await _sut.UpdateMyCompanyAsync(companyId, request, CancellationToken.None);

        Assert.Equal("Nuevo nombre", result.Name);
        Assert.Equal("nuevo@x.dev", result.ContactEmail);
    }

    [Theory]
    [InlineData(CompanyStatus.PENDING_APPROVAL)]
    [InlineData(CompanyStatus.REJECTED)]
    [InlineData(CompanyStatus.SUSPENDED)]
    public async Task UpdateMyCompanyAsync_WhenNotApproved_ThrowsConflict(CompanyStatus currentStatus)
    {
        var companyId = Guid.NewGuid();
        _companyRepository.Setup(r => r.GetByIdAsync(companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Company { Id = companyId, Status = currentStatus });

        var request = new UpdateCompanyRequest { Name = "Nuevo nombre", ContactEmail = "nuevo@x.dev" };

        await Assert.ThrowsAsync<ConflictAppException>(() => _sut.UpdateMyCompanyAsync(companyId, request, CancellationToken.None));
    }
}
