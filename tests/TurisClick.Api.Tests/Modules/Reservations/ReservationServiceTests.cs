using Microsoft.EntityFrameworkCore;
using Moq;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Companies.Entities;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Experiences.Repositories;
using TurisClick.Api.Modules.Reservations.Dtos;
using TurisClick.Api.Modules.Reservations.Entities;
using TurisClick.Api.Modules.Reservations.Repositories;
using TurisClick.Api.Modules.Reservations.Services;
using TurisClick.Api.Shared.Exceptions;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Reservations;

/// <summary>
/// UC-T-08/10, UC-P-12/13 — foco en las validaciones previas a la transacción atómica (que requiere Postgres
/// real y se cubre con un test de integración de concurrencia, ver ReservationsEndpointsTests) y en el
/// aislamiento entre TOURIST/PROVIDER dueños y no-dueños.
/// </summary>
public class ReservationServiceTests
{
    private readonly Mock<IReservationRepository> _reservationRepository = new();
    private readonly Mock<IReservationItemRepository> _reservationItemRepository = new();
    private readonly Mock<IExperienceAvailabilityRepository> _availabilityRepository = new();
    private readonly Mock<ICurrentUserContext> _currentUser = new();
    private readonly ReservationService _sut;

    private readonly Guid _touristId = Guid.NewGuid();
    private readonly Guid _myCompanyId = Guid.NewGuid();
    private readonly Guid _experienceId = Guid.NewGuid();
    private readonly Guid _availabilityId = Guid.NewGuid();

    public ReservationServiceTests()
    {
        var options = new DbContextOptionsBuilder<TurisClickDbContext>().Options;
        var db = new Mock<TurisClickDbContext>(options);

        _currentUser.Setup(c => c.UserId).Returns(_touristId);
        _currentUser.Setup(c => c.CompanyId).Returns(_myCompanyId);

        var ownershipGuard = new Mock<ICompanyOwnershipGuard>();
        ownershipGuard.Setup(g => g.EnsureOwns(It.IsAny<Guid>()))
            .Callback<Guid>(resourceCompanyId =>
            {
                if (resourceCompanyId != _myCompanyId)
                    throw new ForbiddenAppException("No tenés permiso sobre un recurso de otra empresa.");
            });

        _sut = new ReservationService(
            _reservationRepository.Object,
            _reservationItemRepository.Object,
            _availabilityRepository.Object,
            _currentUser.Object,
            ownershipGuard.Object,
            db.Object);
    }

    private ExperienceAvailability BookableAvailability() => new()
    {
        Id = _availabilityId,
        ExperienceId = _experienceId,
        Date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5),
        TotalSlots = 10,
        ReservedSlots = 0,
        Status = AvailabilitySlotStatus.OPEN,
        Experience = new Experience
        {
            Id = _experienceId,
            CompanyId = Guid.NewGuid(),
            Status = PublicationStatus.PUBLISHED,
            Price = 50,
            Currency = "USD"
        }
    };

    [Fact]
    public async Task CreateAsync_AvailabilityNotFound_ThrowsNotFound()
    {
        _availabilityRepository.Setup(r => r.GetByIdWithExperienceAsync(_availabilityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExperienceAvailability?)null);

        var request = new CreateReservationRequest { ExperienceAvailabilityId = _availabilityId, Travelers = 2 };

        await Assert.ThrowsAsync<NotFoundAppException>(() => _sut.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_ExperienceNotPublished_ThrowsNotFound()
    {
        var availability = BookableAvailability();
        availability.Experience!.Status = PublicationStatus.DRAFT;
        _availabilityRepository.Setup(r => r.GetByIdWithExperienceAsync(_availabilityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(availability);

        var request = new CreateReservationRequest { ExperienceAvailabilityId = _availabilityId, Travelers = 2 };

        await Assert.ThrowsAsync<NotFoundAppException>(() => _sut.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_AvailabilityClosed_ThrowsGone()
    {
        var availability = BookableAvailability();
        availability.Status = AvailabilitySlotStatus.CLOSED;
        _availabilityRepository.Setup(r => r.GetByIdWithExperienceAsync(_availabilityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(availability);

        var request = new CreateReservationRequest { ExperienceAvailabilityId = _availabilityId, Travelers = 2 };

        await Assert.ThrowsAsync<GoneAppException>(() => _sut.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_AvailabilityDateInPast_ThrowsGone()
    {
        var availability = BookableAvailability();
        availability.Date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        _availabilityRepository.Setup(r => r.GetByIdWithExperienceAsync(_availabilityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(availability);

        var request = new CreateReservationRequest { ExperienceAvailabilityId = _availabilityId, Travelers = 2 };

        await Assert.ThrowsAsync<GoneAppException>(() => _sut.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task GetByIdForTouristAsync_ReservationNotFound_ThrowsNotFound()
    {
        var id = Guid.NewGuid();
        _reservationRepository.Setup(r => r.GetByIdForReadAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<NotFoundAppException>(() => _sut.GetByIdForTouristAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task GetByIdForTouristAsync_BelongsToAnotherTourist_ThrowsForbidden()
    {
        var reservation = new Reservation { Id = Guid.NewGuid(), TouristId = Guid.NewGuid(), Status = ReservationStatus.PENDING_PAYMENT };
        _reservationRepository.Setup(r => r.GetByIdForReadAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ForbiddenAppException>(() => _sut.GetByIdForTouristAsync(reservation.Id, CancellationToken.None));
    }

    [Fact]
    public async Task GetByIdForTouristAsync_OwnReservation_ReturnsIt()
    {
        var reservation = new Reservation { Id = Guid.NewGuid(), TouristId = _touristId, Status = ReservationStatus.PENDING_PAYMENT };
        _reservationRepository.Setup(r => r.GetByIdForReadAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);

        var result = await _sut.GetByIdForTouristAsync(reservation.Id, CancellationToken.None);

        Assert.Equal(reservation.Id, result.Id);
        Assert.Equal("PENDING_PAYMENT", result.Status);
    }

    [Fact]
    public async Task ListReceivedByCompanyAsync_UserWithoutCompany_ThrowsForbidden()
    {
        _currentUser.Setup(c => c.CompanyId).Returns((Guid?)null);

        await Assert.ThrowsAsync<ForbiddenAppException>(() => _sut.ListReceivedByCompanyAsync(1, 20, CancellationToken.None));
    }

    [Fact]
    public async Task ListReceivedByCompanyAsync_ReturnsItemsForOwnCompany()
    {
        var item = new ReservationItem
        {
            Id = Guid.NewGuid(),
            CompanyId = _myCompanyId,
            ProductType = ProductType.EXPERIENCE,
            Status = ReservationItemStatus.PENDING_PAYMENT,
            Company = new Company { Id = _myCompanyId, Name = "Andes Travel" }
        };
        _reservationItemRepository.Setup(r => r.ListByCompanyAsync(_myCompanyId, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([item], 1));

        var result = await _sut.ListReceivedByCompanyAsync(1, 20, CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("Andes Travel", result.Items[0].CompanyName);
    }

    [Fact]
    public async Task GetReceivedItemByIdAsync_ItemNotFound_ThrowsNotFound()
    {
        var id = Guid.NewGuid();
        _reservationItemRepository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReservationItem?)null);

        await Assert.ThrowsAsync<NotFoundAppException>(() => _sut.GetReceivedItemByIdAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task GetReceivedItemByIdAsync_BelongsToAnotherCompany_ThrowsForbidden()
    {
        var item = new ReservationItem { Id = Guid.NewGuid(), CompanyId = Guid.NewGuid() };
        _reservationItemRepository.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        await Assert.ThrowsAsync<ForbiddenAppException>(() => _sut.GetReceivedItemByIdAsync(item.Id, CancellationToken.None));
    }

    [Fact]
    public async Task GetReceivedItemByIdAsync_OwnCompanyItem_ReturnsIt()
    {
        var item = new ReservationItem
        {
            Id = Guid.NewGuid(),
            CompanyId = _myCompanyId,
            ProductType = ProductType.EXPERIENCE,
            Status = ReservationItemStatus.CONFIRMED,
            Company = new Company { Id = _myCompanyId, Name = "Andes Travel" }
        };
        _reservationItemRepository.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        var result = await _sut.GetReceivedItemByIdAsync(item.Id, CancellationToken.None);

        Assert.Equal("CONFIRMED", result.Status);
    }
}
