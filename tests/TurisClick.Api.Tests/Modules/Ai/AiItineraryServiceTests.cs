using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Ai.Entities;
using TurisClick.Api.Modules.Ai.Repositories;
using TurisClick.Api.Modules.Ai.Services;
using TurisClick.Api.Modules.Categories.Entities;
using TurisClick.Api.Modules.Destinations.Entities;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Reservations.Entities;
using TurisClick.Api.Shared.Exceptions;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Ai;

/// <summary>
/// UC-T-16/17 y UC-AI-06 (Oleada 6) — guardar, listar guardados, retomar y explicar un componente.
/// Guardar NO reserva: no toca cupos ni congela precio (sección 7 de la sesión).
/// </summary>
public class AiItineraryServiceTests
{
    private readonly Mock<IAiItineraryRepository> _itineraryRepository = new();
    private readonly Mock<IAiConversationRepository> _conversationRepository = new();
    private readonly Mock<IAiCatalogRepository> _catalogRepository = new();
    private readonly Mock<IItineraryRevalidationService> _revalidationService = new();
    private readonly Mock<IAiModelClient> _aiModelClient = new();
    private readonly Mock<ICurrentUserContext> _currentUser = new();
    private readonly Mock<TurisClickDbContext> _db;
    private readonly AiItineraryService _sut;

    private readonly Guid _touristId = Guid.NewGuid();
    private readonly Guid _experienceId = Guid.NewGuid();
    private readonly Guid _availabilityId = Guid.NewGuid();

    public AiItineraryServiceTests()
    {
        var options = new DbContextOptionsBuilder<TurisClickDbContext>().Options;
        _db = new Mock<TurisClickDbContext>(options);
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _currentUser.Setup(c => c.UserId).Returns(_touristId);
        _revalidationService.Setup(r => r.RevalidateAsync(It.IsAny<AiItinerary>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ItineraryRevalidationResult.Empty);
        _catalogRepository.Setup(r => r.GetExperiencesByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _catalogRepository.Setup(r => r.GetPackagesByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        _sut = new AiItineraryService(
            _itineraryRepository.Object,
            _conversationRepository.Object,
            _catalogRepository.Object,
            _revalidationService.Object,
            _aiModelClient.Object,
            _currentUser.Object,
            Mock.Of<ILogger<AiItineraryService>>(),
            _db.Object);
    }

    private AiItineraryItem Item() => new()
    {
        Id = Guid.NewGuid(),
        DayNumber = 1,
        ProductType = ProductType.EXPERIENCE,
        ExperienceId = _experienceId,
        ExperienceAvailabilityId = _availabilityId,
        EstimatedUnitPrice = 80,
        Currency = "USD",
        Experience = new Experience { Id = _experienceId, Title = "Salar de Uyuni" }
    };

    private AiItinerary Itinerary(AiItineraryStatus status = AiItineraryStatus.DRAFT, Guid? ownerId = null)
    {
        var itinerary = new AiItinerary
        {
            Id = Guid.NewGuid(),
            AiConversationId = Guid.NewGuid(),
            TouristId = ownerId ?? _touristId,
            Status = status,
            Version = 1,
            Items = [Item()]
        };

        _itineraryRepository.Setup(r => r.GetByIdForReadAsync(itinerary.Id, It.IsAny<CancellationToken>())).ReturnsAsync(itinerary);
        _itineraryRepository.Setup(r => r.GetByIdForUpdateAsync(itinerary.Id, It.IsAny<CancellationToken>())).ReturnsAsync(itinerary);
        return itinerary;
    }

    private AiConversation SetupConversation(Guid conversationId, int travelers = 2, params Category[] interests)
    {
        var conversation = new AiConversation
        {
            Id = conversationId,
            TouristId = _touristId,
            TravelersCount = travelers,
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 5),
            PreferredDestination = new Destination { Id = Guid.NewGuid(), Name = "Uyuni" },
            Categories = [.. interests]
        };

        _conversationRepository.Setup(r => r.GetByIdForReadAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        return conversation;
    }

    // ---- UC-T-16: guardar ----

    [Fact]
    public async Task Save_DraftBecomesSaved()
    {
        var itinerary = Itinerary();
        SetupConversation(itinerary.AiConversationId);

        var result = await _sut.SaveAsync(itinerary.Id, CancellationToken.None);

        Assert.Equal(AiItineraryStatus.SAVED, itinerary.Status);
        Assert.Equal("SAVED", result.Status);
    }

    [Fact]
    public async Task Save_AlreadySaved_IsIdempotent()
    {
        var itinerary = Itinerary(AiItineraryStatus.SAVED);
        SetupConversation(itinerary.AiConversationId);
        var updatedAtBefore = itinerary.UpdatedAt;

        var result = await _sut.SaveAsync(itinerary.Id, CancellationToken.None);

        Assert.Equal("SAVED", result.Status);
        Assert.Equal(updatedAtBefore, itinerary.UpdatedAt); // no-op: no se toca nada
    }

    [Fact]
    public async Task Save_AlreadyBooked_Conflicts()
    {
        var itinerary = Itinerary(AiItineraryStatus.BOOKED);

        await Assert.ThrowsAsync<ConflictAppException>(() => _sut.SaveAsync(itinerary.Id, CancellationToken.None));
        Assert.Equal(AiItineraryStatus.BOOKED, itinerary.Status);
    }

    [Fact]
    public async Task Save_OfAnotherTourist_ThrowsForbidden()
    {
        var itinerary = Itinerary(ownerId: Guid.NewGuid());

        await Assert.ThrowsAsync<ForbiddenAppException>(() => _sut.SaveAsync(itinerary.Id, CancellationToken.None));
        Assert.Equal(AiItineraryStatus.DRAFT, itinerary.Status); // no se modificó nada ajeno
    }

    [Fact]
    public async Task Save_NotFound_ThrowsNotFound()
    {
        var id = Guid.NewGuid();
        _itineraryRepository.Setup(r => r.GetByIdForUpdateAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((AiItinerary?)null);

        await Assert.ThrowsAsync<NotFoundAppException>(() => _sut.SaveAsync(id, CancellationToken.None));
    }

    // ---- UC-T-17: retomar ----

    [Fact]
    public async Task GetById_RevalidatesAndSurfacesWarnings()
    {
        var itinerary = Itinerary(AiItineraryStatus.SAVED);
        SetupConversation(itinerary.AiConversationId);
        var itemId = itinerary.Items.Single().Id;

        _revalidationService.Setup(r => r.RevalidateAsync(It.IsAny<AiItinerary>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItineraryRevalidationResult(
                new Dictionary<Guid, ItemRevalidation>
                {
                    [itemId] = new(itemId, true, true, true, true, 95, "USD", 3, ["El precio cambió de USD 80 a USD 95."])
                },
                ["El precio cambió de USD 80 a USD 95."]));

        var result = await _sut.GetByIdAsync(itinerary.Id, CancellationToken.None);

        // El snapshot NO se toca; el precio vigente viaja al lado. Los dos coexisten.
        var item = Assert.Single(result.Items);
        Assert.Equal(80, item.EstimatedUnitPrice);
        Assert.Equal("USD", item.Currency);
        Assert.Equal(95, item.CurrentPrice);
        Assert.Equal("USD", item.CurrentCurrency);
        Assert.True(item.PriceChanged);
        Assert.Equal("AVAILABLE", item.AvailabilityState);
        Assert.True(item.IsStillAvailable);
        // Y la entidad persistida sigue con su precio original: revalidar es solo leer.
        Assert.Equal(80, itinerary.Items.Single().EstimatedUnitPrice);
        Assert.Contains(result.Warnings, w => w.Contains("95"));
    }

    [Fact]
    public async Task GetById_UnavailableItem_MarksItineraryAsNotBookable()
    {
        var itinerary = Itinerary(AiItineraryStatus.SAVED);
        SetupConversation(itinerary.AiConversationId);
        var itemId = itinerary.Items.Single().Id;

        _revalidationService.Setup(r => r.RevalidateAsync(It.IsAny<AiItinerary>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItineraryRevalidationResult(
                new Dictionary<Guid, ItemRevalidation>
                {
                    [itemId] = new(itemId, true, true, true, false, 80, "USD", 0, ["Se quedó sin cupos."])
                },
                ["Se quedó sin cupos."]));

        var result = await _sut.GetByIdAsync(itinerary.Id, CancellationToken.None);

        Assert.False(result.IsStillBookable);
        var item = Assert.Single(result.Items);
        Assert.False(item.IsStillAvailable);
        Assert.Equal("SOLD_OUT", item.AvailabilityState); // causa estructurada, no solo prosa
        Assert.Equal(0, item.CurrentAvailableSlots);
    }

    [Fact]
    public async Task GetById_NeverWritesToTheDatabase()
    {
        // Sección 9 / precisión del usuario: revalidar al abrir es SOLO lectura. El snapshot histórico
        // no se reescribe nunca, ni siquiera cuando el catálogo cambió.
        var itinerary = Itinerary(AiItineraryStatus.SAVED);
        SetupConversation(itinerary.AiConversationId);
        var itemId = itinerary.Items.Single().Id;

        _revalidationService.Setup(r => r.RevalidateAsync(It.IsAny<AiItinerary>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItineraryRevalidationResult(
                new Dictionary<Guid, ItemRevalidation>
                {
                    [itemId] = new(itemId, true, false, true, true, 999, "USD", 3, ["Ya no está publicada."])
                },
                ["Ya no está publicada."]));

        await _sut.GetByIdAsync(itinerary.Id, CancellationToken.None);
        await _sut.GetByIdAsync(itinerary.Id, CancellationToken.None); // idempotente: leer dos veces no cambia nada

        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(80, itinerary.Items.Single().EstimatedUnitPrice);
        Assert.Equal("USD", itinerary.Items.Single().Currency);
    }

    [Fact]
    public async Task GetById_UnpublishedProduct_ReportsUnpublishedState()
    {
        var itinerary = Itinerary(AiItineraryStatus.SAVED);
        SetupConversation(itinerary.AiConversationId);
        var itemId = itinerary.Items.Single().Id;

        _revalidationService.Setup(r => r.RevalidateAsync(It.IsAny<AiItinerary>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItineraryRevalidationResult(
                new Dictionary<Guid, ItemRevalidation>
                {
                    [itemId] = new(itemId, true, false, true, true, 80, "USD", 3, ["Ya no está publicada."])
                },
                ["Ya no está publicada."]));

        var result = await _sut.GetByIdAsync(itinerary.Id, CancellationToken.None);

        Assert.Equal("UNPUBLISHED", Assert.Single(result.Items).AvailabilityState);
        Assert.False(result.IsStillBookable);
    }

    [Fact]
    public async Task GetById_CurrencyChanged_DoesNotClaimAPriceChange()
    {
        var itinerary = Itinerary(AiItineraryStatus.SAVED);
        SetupConversation(itinerary.AiConversationId);
        var itemId = itinerary.Items.Single().Id;

        _revalidationService.Setup(r => r.RevalidateAsync(It.IsAny<AiItinerary>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItineraryRevalidationResult(
                new Dictionary<Guid, ItemRevalidation>
                {
                    [itemId] = new(itemId, true, true, true, true, 80, "EUR", 3, ["Ahora se vende en EUR."])
                },
                ["Ahora se vende en EUR."]));

        var result = await _sut.GetByIdAsync(itinerary.Id, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("USD", item.Currency);        // snapshot
        Assert.Equal("EUR", item.CurrentCurrency); // vigente
        Assert.False(item.PriceChanged);           // monedas distintas: no se compara ni se convierte
    }

    [Fact]
    public async Task GetById_OfAnotherTourist_ThrowsForbidden()
    {
        var itinerary = Itinerary(ownerId: Guid.NewGuid());

        await Assert.ThrowsAsync<ForbiddenAppException>(() => _sut.GetByIdAsync(itinerary.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ListSavedMine_OnlyAsksForOwnSavedItineraries()
    {
        var itinerary = Itinerary(AiItineraryStatus.SAVED);
        SetupConversation(itinerary.AiConversationId, travelers: 2);
        _itineraryRepository
            .Setup(r => r.ListSavedByTouristAsync(_touristId, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([itinerary], 1));

        var result = await _sut.ListSavedMineAsync(1, 20, CancellationToken.None);

        var row = Assert.Single(result.Items);
        Assert.Equal(itinerary.Id, row.Id);
        Assert.Equal(1, row.ItemCount);
        Assert.Equal(160, Assert.Single(row.Totals).Amount); // 80 × 2 viajeros
        _itineraryRepository.Verify(r => r.ListSavedByTouristAsync(_touristId, 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- UC-AI-06: explicación por componente ----

    [Fact]
    public async Task GetItemExplanation_BuildsFactsFromRealCatalogData()
    {
        var aventura = new Category { Id = Guid.NewGuid(), Name = "Aventura" };
        var itinerary = Itinerary();
        SetupConversation(itinerary.AiConversationId, travelers: 2, aventura);

        _catalogRepository.Setup(r => r.GetExperiencesByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Experience
            {
                Id = _experienceId,
                Title = "Salar de Uyuni",
                Price = 80,
                Currency = "USD",
                DurationMinutes = 240,
                Destination = new Destination { Id = Guid.NewGuid(), Name = "Uyuni" },
                Categories = [aventura],
                Availabilities =
                [
                    new ExperienceAvailability
                    {
                        Id = _availabilityId, ExperienceId = _experienceId,
                        Date = new DateOnly(2026, 10, 3), TotalSlots = 10, ReservedSlots = 2
                    }
                ]
            }]);

        ItemExplanationRequest? request = null;
        _aiModelClient.Setup(c => c.GenerateItemExplanationAsync(It.IsAny<ItemExplanationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ItemExplanationRequest, CancellationToken>((req, _) => request = req)
            .ReturnsAsync("Te lo propuse porque encaja con tus intereses.");

        var result = await _sut.GetItemExplanationAsync(itinerary.Id, itinerary.Items.Single().Id, CancellationToken.None);

        // Todo lo que el modelo puede decir sale de estos hechos, calculados desde Postgres.
        Assert.Equal(result.Facts, request!.Facts);
        Assert.Contains(result.Facts, f => f.Contains("240 minutos"));
        Assert.Contains(result.Facts, f => f.Contains("Uyuni"));
        Assert.Contains(result.Facts, f => f.Contains("Aventura"));
        Assert.Contains(result.Facts, f => f.Contains("2026-10-03") && f.Contains("dentro de tus fechas"));
        Assert.Contains(result.Facts, f => f.Contains("8 cupo"));   // 10 totales − 2 reservados
        Assert.Contains(result.Facts, f => f.Contains("USD 80"));
    }

    [Fact]
    public async Task GetItemExplanation_BudgetInAnotherCurrency_SaysItCannotBeCompared()
    {
        var itinerary = Itinerary();
        var conversation = SetupConversation(itinerary.AiConversationId);
        conversation.BudgetTotal = 1000;
        conversation.BudgetCurrency = "BOB"; // el ítem está en USD

        _aiModelClient.Setup(c => c.GenerateItemExplanationAsync(It.IsAny<ItemExplanationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("...");

        var result = await _sut.GetItemExplanationAsync(itinerary.Id, itinerary.Items.Single().Id, CancellationToken.None);

        // Sección 10: nunca se inventa una conversión para poder comparar.
        Assert.Contains(result.Facts, f => f.Contains("no convierte monedas", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Facts, f => f.Contains("entra") || f.Contains("queda por encima"));
    }

    [Fact]
    public async Task GetItemExplanation_ModelUnavailable_FallsBackToRawFactsWithoutInventing()
    {
        var itinerary = Itinerary();
        SetupConversation(itinerary.AiConversationId);

        _aiModelClient.Setup(c => c.GenerateItemExplanationAsync(It.IsAny<ItemExplanationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiModelUnavailableException("Ollama caído"));

        var result = await _sut.GetItemExplanationAsync(itinerary.Id, itinerary.Items.Single().Id, CancellationToken.None);

        Assert.NotEmpty(result.Facts);
        Assert.All(result.Facts, fact => Assert.Contains(fact, result.Explanation));
    }

    [Fact]
    public async Task GetItemExplanation_ItemFromAnotherItinerary_ThrowsNotFound()
    {
        var itinerary = Itinerary();
        SetupConversation(itinerary.AiConversationId);

        await Assert.ThrowsAsync<NotFoundAppException>(() =>
            _sut.GetItemExplanationAsync(itinerary.Id, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task GetItemExplanation_OfAnotherTourist_ThrowsForbidden()
    {
        var itinerary = Itinerary(ownerId: Guid.NewGuid());

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            _sut.GetItemExplanationAsync(itinerary.Id, itinerary.Items.Single().Id, CancellationToken.None));

        _aiModelClient.Verify(c => c.GenerateItemExplanationAsync(It.IsAny<ItemExplanationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
