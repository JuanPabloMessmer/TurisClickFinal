using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Ai.Dtos;
using TurisClick.Api.Modules.Ai.Entities;
using TurisClick.Api.Modules.Ai.Repositories;
using TurisClick.Api.Modules.Ai.Services;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Packages.Entities;
using TurisClick.Api.Modules.Reservations.Entities;
using TurisClick.Api.Modules.Reservations.Repositories;
using TurisClick.Api.Modules.Reservations.Services;
using TurisClick.Api.Shared.Exceptions;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Ai;

/// <summary>
/// UC-T-18 (Oleada 7) — validación previa al booking: ownership, estados, revalidación final contra el
/// catálogo y política de cambio de precio/moneda. La atomicidad real (holds, rollback, concurrencia)
/// se prueba contra PostgreSQL en AiBookingEndpointsTests: con mocks no se puede demostrar.
/// </summary>
public class AiItineraryBookingServiceTests
{
    private readonly Mock<IAiItineraryRepository> _itineraryRepository = new();
    private readonly Mock<IAiConversationRepository> _conversationRepository = new();
    private readonly Mock<IAiCatalogRepository> _catalogRepository = new();
    private readonly Mock<IReservationRepository> _reservationRepository = new();
    private readonly Mock<IReservationBookingService> _bookingService = new();
    private readonly Mock<IReservationService> _reservationService = new();
    private readonly Mock<ICurrentUserContext> _currentUser = new();
    private readonly TestableBookingService _sut;

    /// <summary>
    /// Sustituye solo la apertura de transacción: todo el resto del servicio (validaciones, cálculo de
    /// líneas, transición de estado) es el código real. La atomicidad se prueba contra PostgreSQL.
    /// </summary>
    private sealed class TestableBookingService(
        IAiItineraryRepository itineraryRepository,
        IAiConversationRepository conversationRepository,
        IAiCatalogRepository catalogRepository,
        IReservationRepository reservationRepository,
        IReservationBookingService reservationBookingService,
        IReservationService reservationService,
        ICurrentUserContext currentUser,
        ILogger<AiItineraryBookingService> logger,
        TurisClickDbContext db)
        : AiItineraryBookingService(itineraryRepository, conversationRepository, catalogRepository,
            reservationRepository, reservationBookingService, reservationService, currentUser, logger, db)
    {
        protected override Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) =>
            Task.FromResult(Mock.Of<IDbContextTransaction>());
    }

    private readonly Guid _touristId = Guid.NewGuid();
    private readonly Guid _experienceId = Guid.NewGuid();
    private readonly Guid _availabilityId = Guid.NewGuid();
    private readonly Guid _companyId = Guid.NewGuid();

    /// <summary>Líneas que el servicio le pasó a la primitiva de reservas.</summary>
    private IReadOnlyList<BookingLine>? _capturedLines;

    public AiItineraryBookingServiceTests()
    {
        var options = new DbContextOptionsBuilder<TurisClickDbContext>().Options;
        var db = new Mock<TurisClickDbContext>(options) { CallBase = false };
        db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _currentUser.Setup(c => c.UserId).Returns(_touristId);
        _catalogRepository.Setup(r => r.GetExperiencesByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _catalogRepository.Setup(r => r.GetPackagesByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _conversationRepository.Setup(r => r.GetByIdForReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiConversation?)null);

        _bookingService
            .Setup(b => b.HoldAndBuildAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyList<BookingLine>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid?, IReadOnlyList<BookingLine>, CancellationToken>((_, _, lines, _) => _capturedLines = lines)
            .ReturnsAsync(new Reservation { Id = Guid.NewGuid() });

        _reservationService
            .Setup(s => s.GetByIdForTouristAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TurisClick.Api.Modules.Reservations.Dtos.ReservationResponse { Status = "PENDING_PAYMENT" });

        _sut = new TestableBookingService(
            _itineraryRepository.Object,
            _conversationRepository.Object,
            _catalogRepository.Object,
            _reservationRepository.Object,
            _bookingService.Object,
            _reservationService.Object,
            _currentUser.Object,
            Mock.Of<ILogger<AiItineraryBookingService>>(),
            db.Object);
    }

    // ---- Helpers ----

    private AiItinerary Itinerary(
        AiItineraryStatus status = AiItineraryStatus.DRAFT,
        Guid? ownerId = null,
        bool withItems = true,
        Guid? availabilityId = null,
        decimal snapshotPrice = 100,
        string snapshotCurrency = "USD")
    {
        var itinerary = new AiItinerary
        {
            Id = Guid.NewGuid(),
            AiConversationId = Guid.NewGuid(),
            TouristId = ownerId ?? _touristId,
            Status = status,
            Version = 1,
            Items = withItems
                ?
                [
                    new AiItineraryItem
                    {
                        Id = Guid.NewGuid(),
                        DayNumber = 1,
                        ProductType = ProductType.EXPERIENCE,
                        ExperienceId = _experienceId,
                        ExperienceAvailabilityId = availabilityId ?? _availabilityId,
                        EstimatedUnitPrice = snapshotPrice,
                        Currency = snapshotCurrency
                    }
                ]
                : []
        };

        _itineraryRepository.Setup(r => r.GetByIdForBookingAsync(itinerary.Id, It.IsAny<CancellationToken>())).ReturnsAsync(itinerary);
        return itinerary;
    }

    private void SetupExperience(
        decimal price = 100, string currency = "USD",
        PublicationStatus status = PublicationStatus.PUBLISHED,
        AvailabilitySlotStatus slotStatus = AvailabilitySlotStatus.OPEN,
        int totalSlots = 10, int reservedSlots = 0,
        bool withAvailability = true,
        int daysFromNow = 30)
    {
        var experience = new Experience
        {
            Id = _experienceId,
            Title = "Salar de Uyuni",
            CompanyId = _companyId,
            Price = price,
            Currency = currency,
            Status = status,
            Availabilities = withAvailability
                ?
                [
                    new ExperienceAvailability
                    {
                        Id = _availabilityId,
                        ExperienceId = _experienceId,
                        Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysFromNow)),
                        TotalSlots = totalSlots,
                        ReservedSlots = reservedSlots,
                        Status = slotStatus
                    }
                ]
                : []
        };

        _catalogRepository
            .Setup(r => r.GetExperiencesByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([experience]);
    }

    private void SetupTravelers(Guid conversationId, int travelers) =>
        _conversationRepository
            .Setup(r => r.GetByIdForReadAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiConversation { Id = conversationId, TouristId = _touristId, TravelersCount = travelers });

    private Task<BookItineraryResponse> BookAsync(AiItinerary itinerary, bool acceptPriceChanges = false) =>
        _sut.BookAsync(itinerary.Id, new BookItineraryRequest { AcceptPriceChanges = acceptPriceChanges }, CancellationToken.None);

    private void VerifyNothingWasBooked() =>
        _bookingService.Verify(
            b => b.HoldAndBuildAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyList<BookingLine>>(), It.IsAny<CancellationToken>()),
            Times.Never);

    // ---- Ownership y estados ----

    [Fact]
    public async Task Book_NotFound_ThrowsNotFound()
    {
        var id = Guid.NewGuid();
        _itineraryRepository.Setup(r => r.GetByIdForBookingAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((AiItinerary?)null);

        await Assert.ThrowsAsync<NotFoundAppException>(() => _sut.BookAsync(id, new BookItineraryRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task Book_OfAnotherTourist_ThrowsForbiddenAndBooksNothing()
    {
        var itinerary = Itinerary(ownerId: Guid.NewGuid());
        SetupExperience();

        await Assert.ThrowsAsync<ForbiddenAppException>(() => BookAsync(itinerary));

        Assert.NotEqual(AiItineraryStatus.BOOKED, itinerary.Status);
        VerifyNothingWasBooked();
    }

    [Fact]
    public async Task Book_DraftItinerary_IsAllowed()
    {
        // UC-T-18 solo exige que el itinerario tenga componentes: guardar no es requisito previo.
        var itinerary = Itinerary(AiItineraryStatus.DRAFT);
        SetupExperience();

        var result = await BookAsync(itinerary);

        Assert.NotNull(result.Reservation);
        Assert.Equal(AiItineraryStatus.BOOKED, itinerary.Status);
    }

    [Fact]
    public async Task Book_SavedItinerary_IsAllowed()
    {
        var itinerary = Itinerary(AiItineraryStatus.SAVED);
        SetupExperience();

        var result = await BookAsync(itinerary);

        Assert.NotNull(result.Reservation);
        Assert.Equal(AiItineraryStatus.BOOKED, itinerary.Status);
    }

    [Fact]
    public async Task Book_AlreadyBooked_ConflictsWithExistingReservationId()
    {
        var itinerary = Itinerary(AiItineraryStatus.BOOKED);
        var existing = new Reservation { Id = Guid.NewGuid() };
        _reservationRepository.Setup(r => r.GetByAiItineraryIdAsync(itinerary.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var ex = await Assert.ThrowsAsync<ConflictAppException>(() => BookAsync(itinerary));

        Assert.Equal(ErrorCodes.ItineraryAlreadyBooked, ex.ErrorCode);
        Assert.Contains(existing.Id.ToString(), ex.Message); // el cliente puede navegar a la reserva existente
        VerifyNothingWasBooked();
    }

    [Fact]
    public async Task Book_DiscardedItinerary_IsRejected()
    {
        var itinerary = Itinerary(AiItineraryStatus.DISCARDED);

        var ex = await Assert.ThrowsAsync<ConflictAppException>(() => BookAsync(itinerary));

        Assert.Equal(ErrorCodes.InvalidItineraryStatus, ex.ErrorCode);
        VerifyNothingWasBooked();
    }

    [Fact]
    public async Task Book_EmptyItinerary_IsRejected()
    {
        var itinerary = Itinerary(withItems: false);

        var ex = await Assert.ThrowsAsync<ValidationAppException>(() => BookAsync(itinerary));

        Assert.Equal(ErrorCodes.ItineraryEmpty, ex.ErrorCode);
        VerifyNothingWasBooked();
    }

    // ---- Revalidación final (UC-SYS-01) ----

    [Fact]
    public async Task Book_ProductNoLongerExists_IsRejected()
    {
        var itinerary = Itinerary();
        // El catálogo no devuelve la experiencia: ya no existe.

        var ex = await Assert.ThrowsAsync<ConflictAppException>(() => BookAsync(itinerary));

        Assert.Equal(ErrorCodes.ProductUnavailable, ex.ErrorCode);
        Assert.NotEqual(AiItineraryStatus.BOOKED, itinerary.Status);
        VerifyNothingWasBooked();
    }

    [Fact]
    public async Task Book_UnpublishedProduct_IsRejected()
    {
        var itinerary = Itinerary();
        SetupExperience(status: PublicationStatus.UNPUBLISHED);

        var ex = await Assert.ThrowsAsync<ConflictAppException>(() => BookAsync(itinerary));

        Assert.Equal(ErrorCodes.ProductUnavailable, ex.ErrorCode);
        VerifyNothingWasBooked();
    }

    [Fact]
    public async Task Book_AvailabilityNoLongerExists_IsRejected()
    {
        var itinerary = Itinerary();
        SetupExperience(withAvailability: false);

        var ex = await Assert.ThrowsAsync<ConflictAppException>(() => BookAsync(itinerary));

        Assert.Equal(ErrorCodes.ProductUnavailable, ex.ErrorCode);
        VerifyNothingWasBooked();
    }

    [Fact]
    public async Task Book_ClosedSlot_IsRejected()
    {
        var itinerary = Itinerary();
        SetupExperience(slotStatus: AvailabilitySlotStatus.CLOSED);

        var ex = await Assert.ThrowsAsync<ConflictAppException>(() => BookAsync(itinerary));

        Assert.Equal(ErrorCodes.ProductUnavailable, ex.ErrorCode);
        VerifyNothingWasBooked();
    }

    [Fact]
    public async Task Book_PastDate_IsRejected()
    {
        var itinerary = Itinerary();
        SetupExperience(daysFromNow: -1);

        var ex = await Assert.ThrowsAsync<ConflictAppException>(() => BookAsync(itinerary));

        Assert.Equal(ErrorCodes.ProductUnavailable, ex.ErrorCode);
        VerifyNothingWasBooked();
    }

    [Fact]
    public async Task Book_SoldOut_IsRejected()
    {
        var itinerary = Itinerary();
        SetupExperience(totalSlots: 10, reservedSlots: 10);

        var ex = await Assert.ThrowsAsync<ConflictAppException>(() => BookAsync(itinerary));

        Assert.Equal(ErrorCodes.InsufficientCapacity, ex.ErrorCode);
        VerifyNothingWasBooked();
    }

    [Fact]
    public async Task Book_InsufficientCapacityForAllTravelers_IsRejected()
    {
        var itinerary = Itinerary();
        SetupTravelers(itinerary.AiConversationId, travelers: 4);
        SetupExperience(totalSlots: 10, reservedSlots: 8); // quedan 2, hacen falta 4

        var ex = await Assert.ThrowsAsync<ConflictAppException>(() => BookAsync(itinerary));

        Assert.Equal(ErrorCodes.InsufficientCapacity, ex.ErrorCode);
        VerifyNothingWasBooked();
    }

    [Fact]
    public async Task Book_ItemWithoutResolvedAvailability_IsRejected()
    {
        // Regla de negocio 14 (domain-model.md): antes de reservar, el slot tiene que estar resuelto.
        var itinerary = Itinerary();
        itinerary.Items.Single().ExperienceAvailabilityId = null;
        SetupExperience();

        var ex = await Assert.ThrowsAsync<ConflictAppException>(() => BookAsync(itinerary));

        Assert.Equal(ErrorCodes.AvailabilityNotResolved, ex.ErrorCode);
        VerifyNothingWasBooked();
    }

    // ---- Precio y moneda (UC-SYS-02) ----

    [Fact]
    public async Task Book_PriceUnchanged_BooksWithoutAskingAnything()
    {
        var itinerary = Itinerary(snapshotPrice: 100);
        SetupExperience(price: 100);

        var result = await BookAsync(itinerary);

        Assert.NotNull(result.Reservation);
        Assert.False(result.RequiresPriceAcceptance);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public async Task Book_PriceChangedNotAccepted_HoldsNothingAndReportsTheChange()
    {
        var itinerary = Itinerary(snapshotPrice: 100);
        SetupExperience(price: 130);

        var result = await BookAsync(itinerary, acceptPriceChanges: false);

        Assert.True(result.RequiresPriceAcceptance);
        Assert.Null(result.Reservation);
        var change = Assert.Single(result.Changes);
        Assert.Equal(ErrorCodes.PriceChanged, change.ChangeType);
        Assert.Equal(100, change.PreviousUnitPrice);
        Assert.Equal(130, change.CurrentUnitPrice);
        Assert.Equal("Salar de Uyuni", change.ProductTitle);
        // Nada se reservó ni se marcó.
        Assert.NotEqual(AiItineraryStatus.BOOKED, itinerary.Status);
        VerifyNothingWasBooked();
    }

    [Fact]
    public async Task Book_PriceChangedAccepted_UsesCurrentPriceAsReservationSnapshot()
    {
        var itinerary = Itinerary(snapshotPrice: 100);
        SetupExperience(price: 130);

        var result = await BookAsync(itinerary, acceptPriceChanges: true);

        Assert.NotNull(result.Reservation);
        // El ReservationItem congela el precio VIGENTE...
        Assert.Equal(130, Assert.Single(_capturedLines!).UnitPrice);
        // ...y el snapshot histórico de la IA queda intacto.
        Assert.Equal(100, itinerary.Items.Single().EstimatedUnitPrice);
        Assert.Single(result.Changes);
    }

    [Fact]
    public async Task Book_CurrencyChanged_IsReportedAsCurrencyChangeNotAsPriceDelta()
    {
        var itinerary = Itinerary(snapshotPrice: 100, snapshotCurrency: "USD");
        SetupExperience(price: 100, currency: "BOB"); // mismo número, otra moneda

        var result = await BookAsync(itinerary, acceptPriceChanges: false);

        Assert.True(result.RequiresPriceAcceptance);
        var change = Assert.Single(result.Changes);
        Assert.Equal(ErrorCodes.CurrencyChanged, change.ChangeType);
        Assert.Equal("USD", change.PreviousCurrency);
        Assert.Equal("BOB", change.CurrentCurrency);
    }

    [Fact]
    public async Task Book_CurrencyChangedAccepted_ReservationUsesCurrentCurrency()
    {
        var itinerary = Itinerary(snapshotPrice: 100, snapshotCurrency: "USD");
        SetupExperience(price: 700, currency: "BOB");

        await BookAsync(itinerary, acceptPriceChanges: true);

        var line = Assert.Single(_capturedLines!);
        Assert.Equal("BOB", line.Currency);
        Assert.Equal(700, line.UnitPrice);
        Assert.Equal("USD", itinerary.Items.Single().Currency); // snapshot de IA intacto
    }

    // ---- Datos derivados en el servidor ----

    [Fact]
    public async Task Book_DerivesCompanyAndTravelersServerSide()
    {
        var itinerary = Itinerary();
        SetupTravelers(itinerary.AiConversationId, travelers: 3);
        SetupExperience();

        await BookAsync(itinerary);

        var line = Assert.Single(_capturedLines!);
        Assert.Equal(_companyId, line.CompanyId);            // del producto real, no del request
        Assert.Equal(3, line.Travelers);                     // de la conversación, no del request
        Assert.Equal(_experienceId, line.ProductId);
        Assert.Equal(_availabilityId, line.AvailabilityId);
        Assert.Equal(1, line.DayNumber);                     // se conserva el orden del viaje
        Assert.Equal(ProductType.EXPERIENCE, line.ProductType);
    }

    [Fact]
    public async Task Book_PassesItineraryIdSoTheReservationKnowsItsOrigin()
    {
        var itinerary = Itinerary();
        SetupExperience();

        await BookAsync(itinerary);

        _bookingService.Verify(b => b.HoldAndBuildAsync(
            _touristId, itinerary.Id, It.IsAny<IReadOnlyList<BookingLine>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Book_MultiItem_SendsOneLinePerItemOrderedByDay()
    {
        var packageId = Guid.NewGuid();
        var packageAvailabilityId = Guid.NewGuid();
        var packageCompanyId = Guid.NewGuid();

        var itinerary = Itinerary();
        itinerary.Items.Add(new AiItineraryItem
        {
            Id = Guid.NewGuid(),
            DayNumber = 2,
            ProductType = ProductType.PACKAGE,
            PackageId = packageId,
            PackageAvailabilityId = packageAvailabilityId,
            EstimatedUnitPrice = 300,
            Currency = "BOB"
        });

        SetupExperience();
        _catalogRepository
            .Setup(r => r.GetPackagesByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Package
            {
                Id = packageId,
                Title = "Uyuni 3 días",
                CompanyId = packageCompanyId,
                Price = 300,
                Currency = "BOB",
                Status = PublicationStatus.PUBLISHED,
                Availabilities =
                [
                    new PackageAvailability
                    {
                        Id = packageAvailabilityId, PackageId = packageId,
                        DepartureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                        TotalSlots = 10, ReservedSlots = 0, Status = AvailabilitySlotStatus.OPEN
                    }
                ]
            }]);

        await BookAsync(itinerary);

        Assert.Equal(2, _capturedLines!.Count);
        Assert.Equal(ProductType.EXPERIENCE, _capturedLines[0].ProductType);
        Assert.Equal(ProductType.PACKAGE, _capturedLines[1].ProductType);
        // Multi-provider y multi-moneda en la misma reserva, sin ninguna conversión.
        Assert.Equal(2, _capturedLines.Select(l => l.CompanyId).Distinct().Count());
        Assert.Equal(["USD", "BOB"], _capturedLines.Select(l => l.Currency));
    }
}
