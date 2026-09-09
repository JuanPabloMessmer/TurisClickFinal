using Moq;
using TurisClick.Api.Modules.Ai.Entities;
using TurisClick.Api.Modules.Ai.Repositories;
using TurisClick.Api.Modules.Ai.Services;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Packages.Entities;
using TurisClick.Api.Modules.Reservations.Entities;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Ai;

/// <summary>
/// UC-T-17 / sección 9 de la sesión: retomar un itinerario guardado NO asume que sigue vigente. Estos
/// tests cubren los casos que tienen que producir warning sin tocar el snapshot persistido.
/// </summary>
public class ItineraryRevalidationServiceTests
{
    private readonly Mock<IAiCatalogRepository> _catalogRepository = new();
    private readonly ItineraryRevalidationService _sut;

    private readonly Guid _experienceId = Guid.NewGuid();
    private readonly Guid _availabilityId = Guid.NewGuid();

    public ItineraryRevalidationServiceTests()
    {
        _catalogRepository.Setup(r => r.GetExperiencesByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _catalogRepository.Setup(r => r.GetPackagesByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _sut = new ItineraryRevalidationService(_catalogRepository.Object);
    }

    private Experience MakeExperience(
        decimal price = 80, string currency = "USD",
        PublicationStatus status = PublicationStatus.PUBLISHED,
        int totalSlots = 10, int reservedSlots = 0,
        AvailabilitySlotStatus slotStatus = AvailabilitySlotStatus.OPEN) => new()
    {
        Id = _experienceId,
        Title = "Salar de Uyuni",
        Price = price,
        Currency = currency,
        Status = status,
        Availabilities =
        [
            new ExperienceAvailability
            {
                Id = _availabilityId,
                ExperienceId = _experienceId,
                Date = new DateOnly(2026, 10, 5),
                TotalSlots = totalSlots,
                ReservedSlots = reservedSlots,
                Status = slotStatus
            }
        ]
    };

    private AiItinerary MakeItinerary(decimal snapshotPrice = 80, string snapshotCurrency = "USD") => new()
    {
        Id = Guid.NewGuid(),
        Items =
        [
            new AiItineraryItem
            {
                Id = Guid.NewGuid(),
                ProductType = ProductType.EXPERIENCE,
                ExperienceId = _experienceId,
                ExperienceAvailabilityId = _availabilityId,
                DayNumber = 1,
                EstimatedUnitPrice = snapshotPrice,
                Currency = snapshotCurrency
            }
        ]
    };

    [Fact]
    public async Task Revalidate_NothingChanged_IsValidWithoutWarnings()
    {
        _catalogRepository.Setup(r => r.GetExperiencesByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeExperience()]);

        var result = await _sut.RevalidateAsync(MakeItinerary(), CancellationToken.None);

        var item = Assert.Single(result.ByItemId.Values);
        Assert.True(item.IsValid);
        Assert.Equal(ItemAvailabilityState.AVAILABLE, item.State);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Revalidate_PriceChanged_ReportsOldAndNewPrice()
    {
        _catalogRepository.Setup(r => r.GetExperiencesByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeExperience(price: 95)]);

        var result = await _sut.RevalidateAsync(MakeItinerary(snapshotPrice: 80), CancellationToken.None);

        var item = Assert.Single(result.ByItemId.Values);
        Assert.True(item.IsValid); // cambió el precio, pero se sigue pudiendo reservar
        Assert.True(item.PriceChanged(80, "USD"));
        Assert.Equal(95, item.CurrentPrice);
        Assert.Contains(result.Warnings, w => w.Contains("80") && w.Contains("95"));
    }

    [Fact]
    public async Task Revalidate_NoCapacityLeft_IsNotValidAndWarns()
    {
        _catalogRepository.Setup(r => r.GetExperiencesByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeExperience(totalSlots: 10, reservedSlots: 10)]);

        var result = await _sut.RevalidateAsync(MakeItinerary(), CancellationToken.None);

        var item = Assert.Single(result.ByItemId.Values);
        Assert.False(item.IsValid);
        Assert.False(item.HasCapacity);
        Assert.Equal(ItemAvailabilityState.SOLD_OUT, item.State);
        Assert.Contains(result.Warnings, w => w.Contains("cupos", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Revalidate_Unpublished_IsNotValidAndWarns()
    {
        _catalogRepository.Setup(r => r.GetExperiencesByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeExperience(status: PublicationStatus.UNPUBLISHED)]);

        var result = await _sut.RevalidateAsync(MakeItinerary(), CancellationToken.None);

        var item = Assert.Single(result.ByItemId.Values);
        Assert.False(item.IsValid);
        Assert.False(item.IsPublished);
        Assert.Equal(ItemAvailabilityState.UNPUBLISHED, item.State);
        Assert.Contains(result.Warnings, w => w.Contains("publicada", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Revalidate_SlotClosed_IsNotValidAndWarns()
    {
        _catalogRepository.Setup(r => r.GetExperiencesByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeExperience(slotStatus: AvailabilitySlotStatus.CLOSED)]);

        var result = await _sut.RevalidateAsync(MakeItinerary(), CancellationToken.None);

        var closed = Assert.Single(result.ByItemId.Values);
        Assert.False(closed.AvailabilityExists);
        Assert.Equal(ItemAvailabilityState.SLOT_CLOSED, closed.State);
        Assert.Contains(result.Warnings, w => w.Contains("disponibilidad", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Revalidate_ProductDeleted_IsNotValid()
    {
        // El repositorio no devuelve el producto: ya no existe en el catálogo.
        var result = await _sut.RevalidateAsync(MakeItinerary(), CancellationToken.None);

        var item = Assert.Single(result.ByItemId.Values);
        Assert.False(item.ProductExists);
        Assert.False(item.IsValid);
        Assert.Equal(ItemAvailabilityState.PRODUCT_NOT_FOUND, item.State);
    }

    [Fact]
    public async Task Revalidate_CurrencyChanged_WarnsWithoutInventingConversion()
    {
        // Sección 10: nunca se convierte entre monedas, así que un cambio de moneda no se traduce a un
        // "subió/bajó X" — solo se informa que ya no son comparables.
        _catalogRepository.Setup(r => r.GetExperiencesByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeExperience(price: 80, currency: "EUR")]);

        var result = await _sut.RevalidateAsync(MakeItinerary(snapshotPrice: 80, snapshotCurrency: "USD"), CancellationToken.None);

        var item = Assert.Single(result.ByItemId.Values);
        Assert.False(item.PriceChanged(80, "USD")); // monedas distintas → no se afirma un cambio de precio
        Assert.Contains(result.Warnings, w => w.Contains("EUR") && w.Contains("USD"));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("cambió de", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Revalidate_Package_UsesDepartureDateAndCapacity()
    {
        var packageId = Guid.NewGuid();
        var slotId = Guid.NewGuid();

        _catalogRepository.Setup(r => r.GetPackagesByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Package
            {
                Id = packageId,
                Title = "Uyuni 3 días",
                Price = 300,
                Currency = "USD",
                Status = PublicationStatus.PUBLISHED,
                Availabilities =
                [
                    new PackageAvailability
                    {
                        Id = slotId, PackageId = packageId,
                        DepartureDate = new DateOnly(2026, 10, 5),
                        TotalSlots = 4, ReservedSlots = 4, Status = AvailabilitySlotStatus.OPEN
                    }
                ]
            }]);

        var itinerary = new AiItinerary
        {
            Id = Guid.NewGuid(),
            Items =
            [
                new AiItineraryItem
                {
                    Id = Guid.NewGuid(), ProductType = ProductType.PACKAGE, PackageId = packageId,
                    PackageAvailabilityId = slotId, DayNumber = 1, EstimatedUnitPrice = 300, Currency = "USD"
                }
            ]
        };

        var result = await _sut.RevalidateAsync(itinerary, CancellationToken.None);

        Assert.False(Assert.Single(result.ByItemId.Values).HasCapacity);
        Assert.Contains(result.Warnings, w => w.Contains("2026-10-05"));
    }
}
