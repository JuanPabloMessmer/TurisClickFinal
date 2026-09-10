using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Companies.Entities;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Experiences.Repositories;
using TurisClick.Api.Modules.Packages.Entities;
using TurisClick.Api.Modules.Packages.Repositories;
using TurisClick.Api.Modules.Reservations.Dtos;
using TurisClick.Api.Modules.Reservations.Entities;
using TurisClick.Api.Modules.Reservations.Payments;
using TurisClick.Api.Modules.Reservations.Repositories;
using TurisClick.Api.Modules.Reservations.Services;
using TurisClick.Api.Shared.Exceptions;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Reservations;

/// <summary>
/// UC-T-08/10, UC-P-12/13, UC-T-19 — foco en las validaciones previas a la transacción atómica (que
/// requiere Postgres real y se cubre con tests de integración, ver ReservationsEndpointsTests) y en el
/// aislamiento entre TOURIST/PROVIDER dueños y no-dueños. El camino feliz de UC-T-19 (aprobado/rechazado,
/// revalidación de precio) también requiere Postgres — solo se cubren acá las validaciones que lanzan
/// ANTES de llamar al gateway/abrir la transacción.
/// </summary>
public class ReservationServiceTests
{
    private readonly Mock<IReservationRepository> _reservationRepository = new();
    private readonly Mock<IReservationItemRepository> _reservationItemRepository = new();
    private readonly Mock<IExperienceAvailabilityRepository> _availabilityRepository = new();
    private readonly Mock<IPackageAvailabilityRepository> _packageAvailabilityRepository = new();
    private readonly Mock<IPaymentGateway> _paymentGateway = new();
    private readonly Mock<ICurrentUserContext> _currentUser = new();
    private readonly ReservationService _sut;

    private readonly Guid _touristId = Guid.NewGuid();
    private readonly Guid _myCompanyId = Guid.NewGuid();
    private readonly Guid _experienceId = Guid.NewGuid();
    private readonly Guid _availabilityId = Guid.NewGuid();
    private readonly Guid _packageId = Guid.NewGuid();
    private readonly Guid _packageAvailabilityId = Guid.NewGuid();

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
            _packageAvailabilityRepository.Object,
            _paymentGateway.Object,
            Mock.Of<IReservationBookingService>(),
            _currentUser.Object,
            ownershipGuard.Object,
            Mock.Of<ILogger<ReservationService>>(),
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

    private PackageAvailability BookablePackageAvailability() => new()
    {
        Id = _packageAvailabilityId,
        PackageId = _packageId,
        DepartureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5),
        TotalSlots = 10,
        ReservedSlots = 0,
        Status = AvailabilitySlotStatus.OPEN,
        Package = new Package
        {
            Id = _packageId,
            CompanyId = Guid.NewGuid(),
            Status = PublicationStatus.PUBLISHED,
            Price = 500,
            Currency = "USD"
        }
    };

    [Fact]
    public async Task CreateAsync_PackageAvailabilityNotFound_ThrowsNotFound()
    {
        _packageAvailabilityRepository.Setup(r => r.GetByIdWithPackageAsync(_packageAvailabilityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackageAvailability?)null);

        var request = new CreateReservationRequest { PackageAvailabilityId = _packageAvailabilityId, Travelers = 2 };

        await Assert.ThrowsAsync<NotFoundAppException>(() => _sut.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_PackageNotPublished_ThrowsNotFound()
    {
        var availability = BookablePackageAvailability();
        availability.Package!.Status = PublicationStatus.DRAFT;
        _packageAvailabilityRepository.Setup(r => r.GetByIdWithPackageAsync(_packageAvailabilityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(availability);

        var request = new CreateReservationRequest { PackageAvailabilityId = _packageAvailabilityId, Travelers = 2 };

        await Assert.ThrowsAsync<NotFoundAppException>(() => _sut.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_PackageAvailabilityClosed_ThrowsGone()
    {
        var availability = BookablePackageAvailability();
        availability.Status = AvailabilitySlotStatus.CLOSED;
        _packageAvailabilityRepository.Setup(r => r.GetByIdWithPackageAsync(_packageAvailabilityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(availability);

        var request = new CreateReservationRequest { PackageAvailabilityId = _packageAvailabilityId, Travelers = 2 };

        await Assert.ThrowsAsync<GoneAppException>(() => _sut.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_PackageAvailabilityDateInPast_ThrowsGone()
    {
        var availability = BookablePackageAvailability();
        availability.DepartureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        _packageAvailabilityRepository.Setup(r => r.GetByIdWithPackageAsync(_packageAvailabilityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(availability);

        var request = new CreateReservationRequest { PackageAvailabilityId = _packageAvailabilityId, Travelers = 2 };

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

    private Reservation PayableReservation(ReservationStatus status = ReservationStatus.PENDING_PAYMENT, DateTimeOffset? expiresAt = null) => new()
    {
        Id = Guid.NewGuid(),
        TouristId = _touristId,
        Status = status,
        ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(30),
        Items =
        [
            new ReservationItem
            {
                Id = Guid.NewGuid(),
                CompanyId = Guid.NewGuid(),
                ProductType = ProductType.EXPERIENCE,
                ExperienceId = _experienceId,
                UnitPrice = 50,
                Currency = "USD",
                Subtotal = 50,
                Status = ReservationItemStatus.PENDING_PAYMENT,
                Experience = new Experience { Id = _experienceId, Price = 50, Currency = "USD" }
            }
        ]
    };

    [Fact]
    public async Task PayAsync_ReservationNotFound_ThrowsNotFound()
    {
        var id = Guid.NewGuid();
        _reservationRepository.Setup(r => r.GetByIdForPaymentAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<NotFoundAppException>(() =>
            _sut.PayAsync(id, new PayReservationRequest { Success = true }, CancellationToken.None));
    }

    [Fact]
    public async Task PayAsync_BelongsToAnotherTourist_ThrowsForbidden()
    {
        var reservation = PayableReservation();
        reservation.TouristId = Guid.NewGuid();
        _reservationRepository.Setup(r => r.GetByIdForPaymentAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            _sut.PayAsync(reservation.Id, new PayReservationRequest { Success = true }, CancellationToken.None));

        _paymentGateway.Verify(g => g.ChargeAsync(It.IsAny<PaymentChargeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(ReservationStatus.CONFIRMED)]
    [InlineData(ReservationStatus.CANCELLED)]
    [InlineData(ReservationStatus.PAYMENT_FAILED)]
    public async Task PayAsync_IncompatibleStatus_ThrowsConflict(ReservationStatus status)
    {
        var reservation = PayableReservation(status);
        _reservationRepository.Setup(r => r.GetByIdForPaymentAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ConflictAppException>(() =>
            _sut.PayAsync(reservation.Id, new PayReservationRequest { Success = true }, CancellationToken.None));

        _paymentGateway.Verify(g => g.ChargeAsync(It.IsAny<PaymentChargeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PayAsync_ExpiredReservation_ThrowsGoneNotConflict()
    {
        // Oleada 8: una reserva expirada ya liberó su cupo (UC-SYS-08), así que el recurso quedó
        // obsoleto y no simplemente "en conflicto" — mismo criterio que el chequeo de ExpiresAt.
        var reservation = PayableReservation(ReservationStatus.EXPIRED);
        _reservationRepository.Setup(r => r.GetByIdForPaymentAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);

        var ex = await Assert.ThrowsAsync<GoneAppException>(() =>
            _sut.PayAsync(reservation.Id, new PayReservationRequest { Success = true }, CancellationToken.None));

        Assert.Equal(ErrorCodes.ReservationNoLongerPayable, ex.ErrorCode);
        _paymentGateway.Verify(g => g.ChargeAsync(It.IsAny<PaymentChargeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PayAsync_Expired_ThrowsGone()
    {
        var reservation = PayableReservation(expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        _reservationRepository.Setup(r => r.GetByIdForPaymentAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);

        await Assert.ThrowsAsync<GoneAppException>(() =>
            _sut.PayAsync(reservation.Id, new PayReservationRequest { Success = true }, CancellationToken.None));

        _paymentGateway.Verify(g => g.ChargeAsync(It.IsAny<PaymentChargeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// UC-SYS-02 — a diferencia de los demás caminos de PayAsync, este devuelve ANTES de llamar al gateway
    /// o abrir la transacción, así que es seguro cubrirlo con mocks (no necesita Postgres real).
    /// </summary>
    [Fact]
    public async Task PayAsync_PriceChangedWithoutAcceptance_DoesNotChargeAndReturnsCurrentPrice()
    {
        var reservation = PayableReservation();
        reservation.Items.Single().Experience!.Price = 999; // vigente != UnitPrice (50) congelado
        _reservationRepository.Setup(r => r.GetByIdForPaymentAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);

        var result = await _sut.PayAsync(reservation.Id, new PayReservationRequest { Success = true, AcceptPriceChanges = false }, CancellationToken.None);

        Assert.True(result.RequiresPriceAcceptance);
        Assert.Equal("PENDING_PAYMENT", result.Status);
        Assert.Null(result.PaymentApproved);
        Assert.True(result.Items[0].PriceChanged);
        Assert.Equal(999, result.Items[0].CurrentUnitPrice);
        Assert.Equal(50, result.Items[0].UnitPrice); // el congelado no se toca sin aceptación explícita
        _paymentGateway.Verify(g => g.ChargeAsync(It.IsAny<PaymentChargeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
