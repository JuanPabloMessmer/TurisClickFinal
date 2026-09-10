using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Reservations.Repositories;
using TurisClick.Api.Modules.Reservations.Services;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Reservations;

/// <summary>
/// UC-SYS-08 — orquestación del lote. Lo que se prueba acá es que un fallo aislado no arrastre al resto
/// del lote y que se respete el tamaño pedido. La liberación real de cupo, la idempotencia y las
/// carreras se prueban contra PostgreSQL en ReservationExpirationTests: con mocks no se demuestran.
/// </summary>
public class ReservationExpirationServiceTests
{
    private readonly Mock<IReservationRepository> _reservationRepository = new();
    private readonly Mock<IReservationBookingService> _bookingService = new();

    /// <summary>Reemplaza solo la apertura de transacción y la expiración individual, para poder observar el orquestador.</summary>
    private sealed class TestableExpirationService(
        IReservationRepository reservationRepository,
        IReservationBookingService bookingService,
        ILogger<ReservationExpirationService> logger,
        TurisClickDbContext db,
        Func<Guid, Task<bool>> expireOne)
        : ReservationExpirationService(reservationRepository, bookingService, logger, db)
    {
        protected override Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) =>
            Task.FromResult(Mock.Of<IDbContextTransaction>());

        public override Task<bool> ExpireAsync(Guid reservationId, CancellationToken ct) => expireOne(reservationId);
    }

    private TestableExpirationService Build(Func<Guid, Task<bool>> expireOne)
    {
        var options = new DbContextOptionsBuilder<TurisClickDbContext>().Options;
        var db = new Mock<TurisClickDbContext>(options) { CallBase = false };

        return new TestableExpirationService(
            _reservationRepository.Object,
            _bookingService.Object,
            Mock.Of<ILogger<ReservationExpirationService>>(),
            db.Object,
            expireOne);
    }

    private void SetupCandidates(params Guid[] ids) =>
        _reservationRepository
            .Setup(r => r.ListExpiredCandidateIdsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ids.ToList());

    [Fact]
    public async Task ExpireDueReservations_NoCandidates_DoesNothing()
    {
        SetupCandidates();
        var sut = Build(_ => Task.FromResult(true));

        Assert.Equal(0, await sut.ExpireDueReservationsAsync(100, CancellationToken.None));
    }

    [Fact]
    public async Task ExpireDueReservations_CountsOnlyTheOnesThatActuallyWonTheTransition()
    {
        var won = Guid.NewGuid();
        var lost = Guid.NewGuid();
        SetupCandidates(won, lost);

        // `lost` la expiró otra instancia entre el SELECT y el UPDATE: no cuenta ni libera nada.
        var sut = Build(id => Task.FromResult(id == won));

        Assert.Equal(1, await sut.ExpireDueReservationsAsync(100, CancellationToken.None));
    }

    [Fact]
    public async Task ExpireDueReservations_OneFailure_DoesNotAbortTheRestOfTheBatch()
    {
        var first = Guid.NewGuid();
        var broken = Guid.NewGuid();
        var last = Guid.NewGuid();
        SetupCandidates(first, broken, last);

        var processed = new List<Guid>();
        var sut = Build(id =>
        {
            processed.Add(id);
            return id == broken
                ? Task.FromException<bool>(new InvalidOperationException("fallo simulado en una reserva"))
                : Task.FromResult(true);
        });

        var expired = await sut.ExpireDueReservationsAsync(100, CancellationToken.None);

        // La reserva rota se saltea y las otras dos se expiran igual: una transacción por reserva.
        Assert.Equal(2, expired);
        Assert.Equal([first, broken, last], processed);
    }

    [Fact]
    public async Task ExpireDueReservations_PassesTheRequestedBatchSizeToTheRepository()
    {
        SetupCandidates();
        var sut = Build(_ => Task.FromResult(true));

        await sut.ExpireDueReservationsAsync(25, CancellationToken.None);

        _reservationRepository.Verify(
            r => r.ListExpiredCandidateIdsAsync(It.IsAny<DateTimeOffset>(), 25, It.IsAny<CancellationToken>()), Times.Once);
    }
}
