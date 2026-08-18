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
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IPasswordHasherService> _passwordHasher = new();
    private readonly Mock<IAuthService> _authService = new();
    private readonly CompanyService _sut;

    public CompanyServiceTests()
    {
        var options = new DbContextOptionsBuilder<TurisClickDbContext>().Options;
        var db = new Mock<TurisClickDbContext>(options);
        db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _sut = new CompanyService(_companyRepository.Object, _userRepository.Object, _passwordHasher.Object, _authService.Object, db.Object);
    }

    private static RegisterProviderRequest ValidRegisterRequest() => new()
    {
        FirstName = "Ana",
        LastName = "Gómez",
        Email = "ana@andestravel.dev",
        Password = "Password123!",
        CompanyName = "Andes Travel Bolivia",
        LegalDocument = "NIT-12345",
        ContactEmail = "contacto@andestravel.dev"
    };

    [Fact]
    public async Task RegisterProviderAsync_NewEmailAndDocument_CreatesCompanyAndProviderPendingApproval()
    {
        _userRepository.Setup(r => r.EmailExistsAsync("ana@andestravel.dev", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _companyRepository.Setup(r => r.LegalDocumentExistsAsync("NIT-12345", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _passwordHasher.Setup(p => p.Hash("Password123!")).Returns("hashed");
        _authService.Setup(a => a.IssueTokensForUserAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthResultResponse { AccessToken = "at", RefreshToken = "rt", User = new UserSummaryResponse() });

        var result = await _sut.RegisterProviderAsync(ValidRegisterRequest(), CancellationToken.None);

        Assert.Equal("at", result.AccessToken);
        Assert.Equal("Andes Travel Bolivia", result.Company.Name);
        Assert.Equal("PENDING_APPROVAL", result.Company.Status);

        _userRepository.Verify(r => r.AddAsync(
            It.Is<User>(u => u.Role == UserRole.PROVIDER && u.CompanyId != null), It.IsAny<CancellationToken>()), Times.Once);
        _companyRepository.Verify(r => r.AddAsync(
            It.Is<Company>(c => c.Status == CompanyStatus.PENDING_APPROVAL), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterProviderAsync_DuplicateEmail_ThrowsConflict()
    {
        _userRepository.Setup(r => r.EmailExistsAsync("ana@andestravel.dev", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<ConflictAppException>(() => _sut.RegisterProviderAsync(ValidRegisterRequest(), CancellationToken.None));
        _companyRepository.Verify(r => r.AddAsync(It.IsAny<Company>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterProviderAsync_DuplicateLegalDocument_ThrowsConflict()
    {
        _userRepository.Setup(r => r.EmailExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _companyRepository.Setup(r => r.LegalDocumentExistsAsync("NIT-12345", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<ConflictAppException>(() => _sut.RegisterProviderAsync(ValidRegisterRequest(), CancellationToken.None));
        _userRepository.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

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
