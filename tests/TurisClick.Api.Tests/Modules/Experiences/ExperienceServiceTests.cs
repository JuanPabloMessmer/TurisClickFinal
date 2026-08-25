using Microsoft.EntityFrameworkCore;
using Moq;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Categories.Entities;
using TurisClick.Api.Modules.Categories.Repositories;
using TurisClick.Api.Modules.Companies.Entities;
using TurisClick.Api.Modules.Companies.Repositories;
using TurisClick.Api.Modules.Destinations.Entities;
using TurisClick.Api.Modules.Destinations.Repositories;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Experiences.Repositories;
using TurisClick.Api.Modules.Experiences.Services;
using TurisClick.Api.Shared.Exceptions;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Experiences;

/// <summary>
/// UC-P-04/05/06 — foco en: ownership estricto (nunca confiar en un company_id del cliente), validación
/// de referencias (Destination/Category), y la regla explícita de publicación (disponibilidad futura).
/// </summary>
public class ExperienceServiceTests
{
    private readonly Mock<IExperienceRepository> _experienceRepository = new();
    private readonly Mock<IDestinationRepository> _destinationRepository = new();
    private readonly Mock<ICategoryRepository> _categoryRepository = new();
    private readonly Mock<ICompanyRepository> _companyRepository = new();
    private readonly Mock<ICurrentUserContext> _currentUser = new();
    private readonly ExperienceService _sut;

    private readonly Guid _myCompanyId = Guid.NewGuid();
    private readonly Guid _cityDestinationId = Guid.NewGuid();

    public ExperienceServiceTests()
    {
        var options = new DbContextOptionsBuilder<TurisClickDbContext>().Options;
        var db = new Mock<TurisClickDbContext>(options);
        db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _currentUser.Setup(c => c.CompanyId).Returns(_myCompanyId);

        // Simula el comportamiento real del guard: solo permite el company_id del usuario autenticado.
        var ownershipGuard = new Mock<ICompanyOwnershipGuard>();
        ownershipGuard.Setup(g => g.EnsureOwns(It.IsAny<Guid>()))
            .Callback<Guid>(resourceCompanyId =>
            {
                if (resourceCompanyId != _myCompanyId)
                    throw new ForbiddenAppException("No tenés permiso sobre un recurso de otra empresa.");
            });

        _destinationRepository.Setup(r => r.GetByIdAsync(_cityDestinationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Destination { Id = _cityDestinationId, Name = "La Paz", Type = DestinationType.CITY });

        _sut = new ExperienceService(
            _experienceRepository.Object,
            _destinationRepository.Object,
            _categoryRepository.Object,
            _companyRepository.Object,
            _currentUser.Object,
            ownershipGuard.Object,
            db.Object);
    }

    private static CreateExperienceRequest ValidCreateRequest(Guid destinationId) => new()
    {
        Title = "City Tour La Paz",
        Description = "Recorrido por el centro histórico de La Paz.",
        DestinationId = destinationId,
        Price = 50,
        Currency = "USD"
    };

    private Experience OwnedExperience(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        CompanyId = _myCompanyId,
        DestinationId = _cityDestinationId,
        Title = "City Tour",
        Description = "Descripción",
        Price = 50,
        Currency = "USD",
        Status = PublicationStatus.DRAFT,
        Company = new Company { Id = _myCompanyId, Name = "Andes Travel" },
        Destination = new Destination { Id = _cityDestinationId, Name = "La Paz", Type = DestinationType.CITY }
    };

    [Fact]
    public async Task CreateAsync_ApprovedCompanyValidData_CreatesDraft()
    {
        _companyRepository.Setup(r => r.GetByIdAsync(_myCompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Company { Id = _myCompanyId, Status = CompanyStatus.APPROVED });
        _experienceRepository.Setup(r => r.GetByIdForReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => OwnedExperience(id));

        var result = await _sut.CreateAsync(ValidCreateRequest(_cityDestinationId), CancellationToken.None);

        Assert.Equal("DRAFT", result.Status);
        _experienceRepository.Verify(r => r.AddAsync(
            It.Is<Experience>(e => e.CompanyId == _myCompanyId && e.Status == PublicationStatus.DRAFT),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_CompanyPendingApproval_ThrowsForbidden()
    {
        _companyRepository.Setup(r => r.GetByIdAsync(_myCompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Company { Id = _myCompanyId, Status = CompanyStatus.PENDING_APPROVAL });

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            _sut.CreateAsync(ValidCreateRequest(_cityDestinationId), CancellationToken.None));

        _experienceRepository.Verify(r => r.AddAsync(It.IsAny<Experience>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_DestinationDoesNotExist_ThrowsValidation()
    {
        _companyRepository.Setup(r => r.GetByIdAsync(_myCompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Company { Id = _myCompanyId, Status = CompanyStatus.APPROVED });

        var missingDestinationId = Guid.NewGuid();

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            _sut.CreateAsync(ValidCreateRequest(missingDestinationId), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_DestinationIsNotCity_ThrowsValidation()
    {
        _companyRepository.Setup(r => r.GetByIdAsync(_myCompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Company { Id = _myCompanyId, Status = CompanyStatus.APPROVED });

        var regionId = Guid.NewGuid();
        _destinationRepository.Setup(r => r.GetByIdAsync(regionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Destination { Id = regionId, Name = "La Paz (depto.)", Type = DestinationType.REGION });

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            _sut.CreateAsync(ValidCreateRequest(regionId), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_UnknownCategoryId_ThrowsValidation()
    {
        _companyRepository.Setup(r => r.GetByIdAsync(_myCompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Company { Id = _myCompanyId, Status = CompanyStatus.APPROVED });

        var request = ValidCreateRequest(_cityDestinationId);
        request.CategoryIds = [Guid.NewGuid()];
        _categoryRepository.Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await Assert.ThrowsAsync<ValidationAppException>(() => _sut.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_MultipleCoverImages_ThrowsValidation()
    {
        _companyRepository.Setup(r => r.GetByIdAsync(_myCompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Company { Id = _myCompanyId, Status = CompanyStatus.APPROVED });

        var request = ValidCreateRequest(_cityDestinationId);
        request.Images =
        [
            new ExperienceImageRequest { Url = "https://example.com/a.jpg", IsCover = true },
            new ExperienceImageRequest { Url = "https://example.com/b.jpg", IsCover = true }
        ];

        await Assert.ThrowsAsync<ValidationAppException>(() => _sut.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_ImagesWithoutExplicitCover_FirstImageBecomesCover()
    {
        _companyRepository.Setup(r => r.GetByIdAsync(_myCompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Company { Id = _myCompanyId, Status = CompanyStatus.APPROVED });

        Experience? captured = null;
        _experienceRepository.Setup(r => r.AddAsync(It.IsAny<Experience>(), It.IsAny<CancellationToken>()))
            .Callback<Experience, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);
        _experienceRepository.Setup(r => r.GetByIdForReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => OwnedExperience(id));

        var request = ValidCreateRequest(_cityDestinationId);
        request.Images =
        [
            new ExperienceImageRequest { Url = "https://example.com/a.jpg", IsCover = false },
            new ExperienceImageRequest { Url = "https://example.com/b.jpg", IsCover = false }
        ];

        await _sut.CreateAsync(request, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.True(captured!.Images.Single(i => i.Url == "https://example.com/a.jpg").IsCover);
        Assert.False(captured.Images.Single(i => i.Url == "https://example.com/b.jpg").IsCover);
    }

    [Fact]
    public async Task UpdateAsync_ExperienceBelongsToAnotherCompany_ThrowsForbidden()
    {
        var otherCompanyExperience = OwnedExperience();
        otherCompanyExperience.CompanyId = Guid.NewGuid(); // otra empresa
        _experienceRepository.Setup(r => r.GetByIdForUpdateAsync(otherCompanyExperience.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherCompanyExperience);

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            _sut.UpdateAsync(otherCompanyExperience.Id, ValidUpdateRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_ExperienceNotFound_ThrowsNotFound()
    {
        var missingId = Guid.NewGuid();
        _experienceRepository.Setup(r => r.GetByIdForUpdateAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Experience?)null);

        await Assert.ThrowsAsync<NotFoundAppException>(() =>
            _sut.UpdateAsync(missingId, ValidUpdateRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task PublishAsync_WithoutFutureAvailability_ThrowsConflict()
    {
        var experience = OwnedExperience();
        _experienceRepository.Setup(r => r.GetByIdForUpdateAsync(experience.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(experience);
        _experienceRepository.Setup(r => r.HasFutureOpenAvailabilityAsync(experience.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<ConflictAppException>(() => _sut.PublishAsync(experience.Id, CancellationToken.None));
    }

    [Fact]
    public async Task PublishAsync_WithFutureAvailability_Succeeds()
    {
        var experience = OwnedExperience();
        _experienceRepository.Setup(r => r.GetByIdForUpdateAsync(experience.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(experience);
        _experienceRepository.Setup(r => r.HasFutureOpenAvailabilityAsync(experience.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _experienceRepository.Setup(r => r.GetByIdForReadAsync(experience.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(experience);

        var result = await _sut.PublishAsync(experience.Id, CancellationToken.None);

        Assert.Equal(PublicationStatus.PUBLISHED, experience.Status);
    }

    [Fact]
    public async Task PublishAsync_ExperienceBelongsToAnotherCompany_ThrowsForbidden()
    {
        var otherCompanyExperience = OwnedExperience();
        otherCompanyExperience.CompanyId = Guid.NewGuid();
        _experienceRepository.Setup(r => r.GetByIdForUpdateAsync(otherCompanyExperience.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherCompanyExperience);

        await Assert.ThrowsAsync<ForbiddenAppException>(() => _sut.PublishAsync(otherCompanyExperience.Id, CancellationToken.None));
    }

    [Fact]
    public async Task GetPublishedByIdAsync_ExperienceIsDraft_ThrowsNotFound()
    {
        var experience = OwnedExperience();
        experience.Status = PublicationStatus.DRAFT;
        _experienceRepository.Setup(r => r.GetByIdForReadAsync(experience.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(experience);

        await Assert.ThrowsAsync<NotFoundAppException>(() => _sut.GetPublishedByIdAsync(experience.Id, CancellationToken.None));
    }

    [Fact]
    public async Task GetPublishedByIdAsync_ExperienceIsPublished_ReturnsIt()
    {
        var experience = OwnedExperience();
        experience.Status = PublicationStatus.PUBLISHED;
        _experienceRepository.Setup(r => r.GetByIdForReadAsync(experience.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(experience);

        var result = await _sut.GetPublishedByIdAsync(experience.Id, CancellationToken.None);

        Assert.Equal("PUBLISHED", result.Status);
    }

    private UpdateExperienceRequest ValidUpdateRequest() => new()
    {
        Title = "City Tour actualizado",
        Description = "Nueva descripción del recorrido.",
        DestinationId = _cityDestinationId,
        Price = 60,
        Currency = "USD"
    };
}
