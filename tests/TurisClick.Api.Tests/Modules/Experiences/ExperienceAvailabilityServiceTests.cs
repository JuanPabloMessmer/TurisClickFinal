using Microsoft.EntityFrameworkCore;
using Moq;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Experiences.Repositories;
using TurisClick.Api.Modules.Experiences.Services;
using TurisClick.Api.Shared.Exceptions;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Experiences;

/// <summary>UC-P-10 — duplicados, fechas pasadas y ownership.</summary>
public class ExperienceAvailabilityServiceTests
{
    private readonly Mock<IExperienceAvailabilityRepository> _availabilityRepository = new();
    private readonly Mock<IExperienceRepository> _experienceRepository = new();
    private readonly ExperienceAvailabilityService _sut;

    private readonly Guid _myCompanyId = Guid.NewGuid();
    private readonly Guid _experienceId = Guid.NewGuid();

    public ExperienceAvailabilityServiceTests()
    {
        var options = new DbContextOptionsBuilder<TurisClickDbContext>().Options;
        var db = new Mock<TurisClickDbContext>(options);
        db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var ownershipGuard = new Mock<ICompanyOwnershipGuard>();
        ownershipGuard.Setup(g => g.EnsureOwns(It.IsAny<Guid>()))
            .Callback<Guid>(resourceCompanyId =>
            {
                if (resourceCompanyId != _myCompanyId)
                    throw new ForbiddenAppException("No tenés permiso sobre un recurso de otra empresa.");
            });

        _experienceRepository.Setup(r => r.GetByIdAsync(_experienceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Experience { Id = _experienceId, CompanyId = _myCompanyId, Status = PublicationStatus.DRAFT });

        _sut = new ExperienceAvailabilityService(_availabilityRepository.Object, _experienceRepository.Object, ownershipGuard.Object, db.Object);
    }

    [Fact]
    public async Task CreateAsync_PastDate_ThrowsValidation()
    {
        var request = new CreateExperienceAvailabilityRequest
        {
            Date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            TotalSlots = 10
        };

        await Assert.ThrowsAsync<ValidationAppException>(() => _sut.CreateAsync(_experienceId, request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_DuplicateSlot_ThrowsConflict()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5);
        _availabilityRepository.Setup(r => r.ExistsAsync(_experienceId, date, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new CreateExperienceAvailabilityRequest { Date = date, TotalSlots = 10 };

        await Assert.ThrowsAsync<ConflictAppException>(() => _sut.CreateAsync(_experienceId, request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_ValidSlot_Succeeds()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5);
        _availabilityRepository.Setup(r => r.ExistsAsync(_experienceId, date, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var request = new CreateExperienceAvailabilityRequest { Date = date, TotalSlots = 15 };

        var result = await _sut.CreateAsync(_experienceId, request, CancellationToken.None);

        Assert.Equal(15, result.TotalSlots);
        Assert.Equal(15, result.AvailableSlots);
        Assert.Equal("OPEN", result.Status);
        _availabilityRepository.Verify(r => r.AddAsync(
            It.Is<ExperienceAvailability>(a => a.ExperienceId == _experienceId && a.TotalSlots == 15),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_ExperienceBelongsToAnotherCompany_ThrowsForbidden()
    {
        var otherExperienceId = Guid.NewGuid();
        _experienceRepository.Setup(r => r.GetByIdAsync(otherExperienceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Experience { Id = otherExperienceId, CompanyId = Guid.NewGuid() });

        var request = new CreateExperienceAvailabilityRequest
        {
            Date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5),
            TotalSlots = 10
        };

        await Assert.ThrowsAsync<ForbiddenAppException>(() => _sut.CreateAsync(otherExperienceId, request, CancellationToken.None));
    }

    [Fact]
    public async Task ListPublicAsync_ExperienceNotPublished_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundAppException>(() => _sut.ListPublicAsync(_experienceId, CancellationToken.None));
    }

    [Fact]
    public async Task ListPublicAsync_ExperiencePublished_ReturnsBookableSlots()
    {
        _experienceRepository.Setup(r => r.GetByIdAsync(_experienceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Experience { Id = _experienceId, CompanyId = _myCompanyId, Status = PublicationStatus.PUBLISHED });
        _availabilityRepository.Setup(r => r.ListBookableAsync(_experienceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ExperienceAvailability { Id = Guid.NewGuid(), ExperienceId = _experienceId, TotalSlots = 5 }]);

        var result = await _sut.ListPublicAsync(_experienceId, CancellationToken.None);

        Assert.Single(result);
    }
}
