using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Ai.Dtos;
using TurisClick.Api.Modules.Ai.Entities;
using TurisClick.Api.Modules.Ai.Repositories;
using TurisClick.Api.Modules.Ai.Services;
using TurisClick.Api.Modules.Categories.Repositories;
using TurisClick.Api.Modules.Destinations.Entities;
using TurisClick.Api.Modules.Destinations.Repositories;
using TurisClick.Api.Modules.Reservations.Entities;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Ai;

/// <summary>
/// UC-T-15 / UC-AI-05 (Oleada 6) — iterar sobre la propuesta vigente. El foco está en las dos reglas
/// que definen la oleada: preservar lo que el turista no pidió cambiar (sección 2) y revalidar TODO
/// contra Postgres, incluidos los ítems preservados (sección 3).
/// </summary>
public class AiConversationIterationTests
{
    private readonly Mock<IAiConversationRepository> _conversationRepository = new();
    private readonly Mock<IAiItineraryRepository> _itineraryRepository = new();
    private readonly Mock<IDestinationRepository> _destinationRepository = new();
    private readonly Mock<ICategoryRepository> _categoryRepository = new();
    private readonly Mock<IAiModelClient> _aiModelClient = new();
    private readonly Mock<IRetrievalService> _retrievalService = new();
    private readonly Mock<IItineraryRevalidationService> _revalidationService = new();
    private readonly Mock<ICurrentUserContext> _currentUser = new();
    private readonly AiConversationService _sut;

    private readonly Guid _touristId = Guid.NewGuid();

    /// <summary>Última propuesta que el Service intentó persistir.</summary>
    private AiItinerary? _persisted;

    public AiConversationIterationTests()
    {
        var options = new DbContextOptionsBuilder<TurisClickDbContext>().Options;
        var db = new Mock<TurisClickDbContext>(options);
        db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        db.Setup(d => d.Set<AiMessage>()).Returns(Mock.Of<DbSet<AiMessage>>());

        _currentUser.Setup(c => c.UserId).Returns(_touristId);
        _destinationRepository.Setup(r => r.ListAsync(null, DestinationType.CITY, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _categoryRepository.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        // El mensaje no aporta preferencias nuevas: lo relevante del turno es el ajuste.
        _aiModelClient.Setup(c => c.ExtractPreferencesAsync(It.IsAny<PreferenceExtractionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreferenceExtractionResult(null, [], null, null, null, null, null, null, false, null));

        _retrievalService.Setup(r => r.RetrieveAsync(It.IsAny<RetrievalQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RetrievalResult([], []));

        _itineraryRepository.Setup(r => r.AddAsync(It.IsAny<AiItinerary>(), It.IsAny<CancellationToken>()))
            .Callback<AiItinerary, CancellationToken>((it, _) => _persisted = it)
            .Returns(Task.CompletedTask);
        _itineraryRepository.Setup(r => r.GetByIdForReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _persisted);

        _sut = new AiConversationService(
            _conversationRepository.Object,
            _itineraryRepository.Object,
            _destinationRepository.Object,
            _categoryRepository.Object,
            _aiModelClient.Object,
            _retrievalService.Object,
            _revalidationService.Object,
            _currentUser.Object,
            Mock.Of<ILogger<AiConversationService>>(),
            db.Object);
    }

    // ---- Helpers ----

    /// <summary>Conversación con todo lo obligatorio ya resuelto: el siguiente mensaje es un ajuste, no clarificación.</summary>
    private AiConversation ConversationReadyToSearch()
    {
        var conversation = new AiConversation
        {
            Id = Guid.NewGuid(),
            TouristId = _touristId,
            Status = AiConversationStatus.ACTIVE,
            PreferredDestinationId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 10, 5),
            EndDate = new DateOnly(2026, 10, 8),
            TravelersCount = 2,
            Messages = [],
            Categories = []
        };

        _conversationRepository.Setup(r => r.GetByIdForUpdateAsync(conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        return conversation;
    }

    private static AiItineraryItem ExperienceItem(int day, decimal price = 50, string currency = "USD") => new()
    {
        Id = Guid.NewGuid(),
        DayNumber = day,
        ProductType = ProductType.EXPERIENCE,
        ExperienceId = Guid.NewGuid(),
        EstimatedUnitPrice = price,
        Currency = currency
    };

    private void SetupCurrentItinerary(AiConversation conversation, params AiItineraryItem[] items) =>
        _itineraryRepository
            .Setup(r => r.GetLatestByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiItinerary
            {
                Id = Guid.NewGuid(),
                AiConversationId = conversation.Id,
                TouristId = _touristId,
                Version = 1,
                Status = AiItineraryStatus.DRAFT,
                Items = [.. items]
            });

    private void SetupIntent(ModificationAction action, IEnumerable<Guid>? targets = null, IEnumerable<int>? days = null) =>
        _aiModelClient
            .Setup(c => c.InterpretModificationAsync(It.IsAny<ModificationInterpretationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModificationIntentResult(action, targets?.ToList() ?? [], days?.ToList() ?? [], []));

    /// <summary>Los ítems indicados siguen válidos y al mismo precio.</summary>
    private void SetupRevalidationValid(params AiItineraryItem[] items) =>
        _revalidationService
            .Setup(r => r.RevalidateAsync(It.IsAny<AiItinerary>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItineraryRevalidationResult(
                items.ToDictionary(
                    i => i.Id,
                    i => new ItemRevalidation(i.Id, true, true, true, true, i.EstimatedUnitPrice, i.Currency, 5, [])),
                []));

    private void SetupRevalidation(params ItemRevalidation[] results) =>
        _revalidationService
            .Setup(r => r.RevalidateAsync(It.IsAny<AiItinerary>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItineraryRevalidationResult(results.ToDictionary(r => r.ItemId), []));

    private Task<SendMessageResponse> SendAsync(AiConversation conversation, string message) =>
        _sut.SendMessageAsync(conversation.Id, new SendMessageRequest { Content = message }, CancellationToken.None);

    // ---- Tests ----

    [Fact]
    public async Task RemoveItem_KeepsTheRest_BumpsVersion_AndDoesNotSearchAgain()
    {
        var conversation = ConversationReadyToSearch();
        var keep = ExperienceItem(1);
        var drop = ExperienceItem(2);
        SetupCurrentItinerary(conversation, keep, drop);
        SetupRevalidationValid(keep);
        SetupIntent(ModificationAction.REMOVE, [drop.Id]);

        await SendAsync(conversation, "Quitá lo del día 2.");

        Assert.NotNull(_persisted);
        Assert.Equal(2, _persisted!.Version); // versión nueva; la anterior queda intacta para trazabilidad
        Assert.Equal(keep.ExperienceId, Assert.Single(_persisted.Items).ExperienceId);
        // Un REMOVE puro no necesita candidatos: no vuelve a ser una búsqueda desde cero.
        _retrievalService.Verify(r => r.RetrieveAsync(It.IsAny<RetrievalQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReplaceOneDay_PreservesUnaffectedItems_AndUsesRealCandidate()
    {
        var conversation = ConversationReadyToSearch();
        var keep = ExperienceItem(1);
        var replace = ExperienceItem(2);
        SetupCurrentItinerary(conversation, keep, replace);
        SetupRevalidationValid(keep);
        SetupIntent(ModificationAction.REPLACE, [replace.Id], [2]);

        var replacementId = Guid.NewGuid();
        _retrievalService.Setup(r => r.RetrieveAsync(It.IsAny<RetrievalQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RetrievalResult([new CandidateExperience(replacementId, "Museo", "La Paz", 25, "USD", [], null, [])], []));

        ItineraryCompositionRequest? compositionRequest = null;
        _aiModelClient.Setup(c => c.ComposeItineraryAsync(It.IsAny<ItineraryCompositionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ItineraryCompositionRequest, CancellationToken>((req, _) => compositionRequest = req)
            .ReturnsAsync(new ItineraryCompositionResult("Viaje", [new ComposedItem(2, "EXPERIENCE", replacementId, null)], "Cambié el día 2."));

        await SendAsync(conversation, "Cambiá el día 2.");

        // Al compositor se le dice explícitamente qué conservar y cuál fue el pedido.
        Assert.Equal(keep.ExperienceId, Assert.Single(compositionRequest!.PreservedItems).ProductId);
        Assert.Equal("Cambiá el día 2.", compositionRequest.ModificationInstruction);

        Assert.NotNull(_persisted);
        Assert.Equal(2, _persisted!.Items.Count);
        Assert.Contains(_persisted.Items, i => i.ExperienceId == keep.ExperienceId);     // no se regeneró
        Assert.Contains(_persisted.Items, i => i.ExperienceId == replacementId);         // reemplazo real
        Assert.DoesNotContain(_persisted.Items, i => i.ExperienceId == replace.ExperienceId);
    }

    [Fact]
    public async Task ReplacedProduct_IsExcludedFromRetrieval()
    {
        var conversation = ConversationReadyToSearch();
        var keep = ExperienceItem(1);
        var replace = ExperienceItem(2);
        SetupCurrentItinerary(conversation, keep, replace);
        SetupRevalidationValid(keep);
        SetupIntent(ModificationAction.REPLACE, [replace.Id], [2]);

        RetrievalQuery? query = null;
        _retrievalService.Setup(r => r.RetrieveAsync(It.IsAny<RetrievalQuery>(), It.IsAny<CancellationToken>()))
            .Callback<RetrievalQuery, CancellationToken>((q, _) => query = q)
            .ReturnsAsync(new RetrievalResult([], []));

        await SendAsync(conversation, "No quiero eso, cambialo.");

        // Ni lo que sacó ni lo que ya tiene se vuelve a ofrecer (no re-proponer lo rechazado, no duplicar).
        Assert.Contains(replace.ExperienceId!.Value, query!.ExcludedProductIds);
        Assert.Contains(keep.ExperienceId!.Value, query.ExcludedProductIds);
    }

    [Fact]
    public async Task ModificationTargetingUnknownItemId_IsIgnored()
    {
        var conversation = ConversationReadyToSearch();
        var item = ExperienceItem(1);
        SetupCurrentItinerary(conversation, item);
        SetupRevalidationValid(item);
        // El modelo devuelve un id que nunca estuvo en el itinerario: no puede sacar nada.
        SetupIntent(ModificationAction.REMOVE, [Guid.NewGuid()]);

        await SendAsync(conversation, "Quitá eso.");

        Assert.NotNull(_persisted);
        Assert.Equal(item.ExperienceId, Assert.Single(_persisted!.Items).ExperienceId);
    }

    [Fact]
    public async Task HallucinatedReplacement_IsRejected_ButPreservedItemsSurvive()
    {
        var conversation = ConversationReadyToSearch();
        var keep = ExperienceItem(1);
        var replace = ExperienceItem(2);
        SetupCurrentItinerary(conversation, keep, replace);
        SetupRevalidationValid(keep);
        SetupIntent(ModificationAction.REPLACE, [replace.Id], [2]);

        _retrievalService.Setup(r => r.RetrieveAsync(It.IsAny<RetrievalQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RetrievalResult([new CandidateExperience(Guid.NewGuid(), "Museo", "La Paz", 25, "USD", [], null, [])], []));

        // El modelo propone un id que no estaba entre los candidatos ofrecidos.
        _aiModelClient.Setup(c => c.ComposeItineraryAsync(It.IsAny<ItineraryCompositionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItineraryCompositionResult("Viaje", [new ComposedItem(2, "EXPERIENCE", Guid.NewGuid(), null)], "..."));

        var result = await SendAsync(conversation, "Cambiá el día 2.");

        Assert.NotNull(_persisted);
        Assert.Equal(keep.ExperienceId, Assert.Single(_persisted!.Items).ExperienceId); // el alucinado no se persiste
        Assert.Contains(result.Warnings, w => w.Contains("descartó", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, w => w.Contains("mantuve el resto", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PreservedItemThatBecameUnavailable_IsDroppedWithWarning()
    {
        var conversation = ConversationReadyToSearch();
        var keep = ExperienceItem(1);
        var drop = ExperienceItem(2);
        SetupCurrentItinerary(conversation, keep, drop);
        SetupIntent(ModificationAction.REMOVE, [drop.Id]);

        // El ítem que se iba a conservar se quedó sin cupos entre una versión y la otra.
        SetupRevalidation(new ItemRevalidation(
            keep.Id, ProductExists: true, IsPublished: true, AvailabilityExists: true, HasCapacity: false,
            CurrentPrice: 50, CurrentCurrency: "USD", AvailableSlots: 0, Warnings: ["Se quedó sin cupos para el 2026-10-05."]));

        var result = await SendAsync(conversation, "Quitá lo del día 2.");

        Assert.Contains(result.Warnings, w => w.Contains("cupos", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, w => w.Contains("ya no se puede reservar", StringComparison.OrdinalIgnoreCase));
        // Nunca se mantiene ciegamente un ítem inválido; sin nada válido no se persiste una propuesta vacía.
        _itineraryRepository.Verify(r => r.AddAsync(It.IsAny<AiItinerary>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PreservedItemWithNewPrice_KeepsItsOriginalSnapshot()
    {
        var conversation = ConversationReadyToSearch();
        var keep = ExperienceItem(1, price: 80);
        var drop = ExperienceItem(2);
        SetupCurrentItinerary(conversation, keep, drop);
        SetupIntent(ModificationAction.REMOVE, [drop.Id]);

        SetupRevalidation(new ItemRevalidation(
            keep.Id, true, true, true, true,
            CurrentPrice: 95, CurrentCurrency: "USD", AvailableSlots: 5,
            Warnings: ["El precio de \"Rafting\" cambió de USD 80 a USD 95."]));

        var result = await SendAsync(conversation, "Quitá lo del día 2.");

        // Preservar es preservar: el ítem conserva SU snapshot (80), no se repricea a 95. El precio
        // vigente no se persiste — se expone por DTO al leer, así el cambio queda visible siempre.
        Assert.Equal(80, Assert.Single(_persisted!.Items).EstimatedUnitPrice);
        Assert.Equal("USD", _persisted.Items.Single().Currency);
        Assert.Contains(result.Warnings, w => w.Contains("95")); // pero el cambio sí se avisa
    }

    [Fact]
    public async Task PreservedItem_KeepsProductDayAvailabilityAndSnapshotIntact()
    {
        var conversation = ConversationReadyToSearch();
        var availabilityId = Guid.NewGuid();
        var keep = ExperienceItem(1, price: 40);
        keep.ExperienceAvailabilityId = availabilityId;
        var drop = ExperienceItem(2);
        SetupCurrentItinerary(conversation, keep, drop);
        SetupRevalidationValid(keep);
        SetupIntent(ModificationAction.REMOVE, [drop.Id]);

        await SendAsync(conversation, "Quitá lo del día 2.");

        // Un ítem no afectado por la instrucción viaja idéntico a la versión nueva: mismo producto,
        // mismo día, mismo slot, mismo precio y moneda. Nada se "recompone desde cero".
        var preserved = Assert.Single(_persisted!.Items);
        Assert.Equal(keep.ExperienceId, preserved.ExperienceId);
        Assert.Equal(keep.DayNumber, preserved.DayNumber);
        Assert.Equal(availabilityId, preserved.ExperienceAvailabilityId);
        Assert.Equal(40, preserved.EstimatedUnitPrice);
        Assert.Equal(keep.Currency, preserved.Currency);
        Assert.Equal(ProductType.EXPERIENCE, preserved.ProductType);
    }

    [Fact]
    public async Task ValidPreservedItem_IsNeverReplacedEvenWhenBetterCandidatesExist()
    {
        var conversation = ConversationReadyToSearch();
        var keep = ExperienceItem(1);
        var replace = ExperienceItem(2);
        SetupCurrentItinerary(conversation, keep, replace);
        SetupRevalidationValid(keep);
        SetupIntent(ModificationAction.REPLACE, [replace.Id], [2]);

        // Hay candidatos disponibles, pero solo pueden ocupar el hueco que dejó el ítem reemplazado.
        var replacementId = Guid.NewGuid();
        _retrievalService.Setup(r => r.RetrieveAsync(It.IsAny<RetrievalQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RetrievalResult([new CandidateExperience(replacementId, "Museo", "La Paz", 25, "USD", [], null, [])], []));

        // El modelo intenta pisar TAMBIÉN el día 1 (que estaba preservado).
        _aiModelClient.Setup(c => c.ComposeItineraryAsync(It.IsAny<ItineraryCompositionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItineraryCompositionResult("Viaje",
                [
                    new ComposedItem(1, "EXPERIENCE", replacementId, null),
                    new ComposedItem(2, "EXPERIENCE", replacementId, null)
                ], "..."));

        await SendAsync(conversation, "Cambiá el día 2.");

        // El día 1 queda exactamente como estaba: el backend reinserta el preservado y descarta lo que
        // el modelo quiso meter encima. Solo el día 2 (lo que el turista pidió cambiar) se recompone.
        var dayOne = Assert.Single(_persisted!.Items, i => i.DayNumber == 1);
        Assert.Equal(keep.ExperienceId, dayOne.ExperienceId);
        Assert.Equal(replacementId, Assert.Single(_persisted.Items, i => i.DayNumber == 2).ExperienceId);
        Assert.Equal(2, _persisted.Items.Count);
    }

    [Fact]
    public async Task ReduceBudget_OnlyOffersCheaperCandidatesInTheSameCurrency()
    {
        var conversation = ConversationReadyToSearch();
        var cheap = ExperienceItem(1, price: 30);
        var expensive = ExperienceItem(2, price: 120);
        SetupCurrentItinerary(conversation, cheap, expensive);
        SetupRevalidationValid(cheap);
        SetupIntent(ModificationAction.REDUCE_BUDGET);

        var cheaperId = Guid.NewGuid();
        _retrievalService.Setup(r => r.RetrieveAsync(It.IsAny<RetrievalQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RetrievalResult(
                [
                    new CandidateExperience(cheaperId, "Museo", "La Paz", 40, "USD", [], null, []),           // más barato, misma moneda
                    new CandidateExperience(Guid.NewGuid(), "Tour caro", "La Paz", 200, "USD", [], null, []), // más caro
                    new CandidateExperience(Guid.NewGuid(), "Tour EUR", "La Paz", 10, "EUR", [], null, [])    // barato, pero otra moneda
                ], []));

        ItineraryCompositionRequest? compositionRequest = null;
        _aiModelClient.Setup(c => c.ComposeItineraryAsync(It.IsAny<ItineraryCompositionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ItineraryCompositionRequest, CancellationToken>((req, _) => compositionRequest = req)
            .ReturnsAsync(new ItineraryCompositionResult("Viaje", [new ComposedItem(2, "EXPERIENCE", cheaperId, null)], "Más barato."));

        await SendAsync(conversation, "Quiero gastar menos.");

        // Solo sobrevive el estrictamente más barato en la MISMA moneda: comparar contra EUR exigiría
        // una conversión que TurisClick no hace.
        Assert.Equal(cheaperId, Assert.Single(compositionRequest!.CandidateExperiences).Id);
        // Y el componente más caro salió del itinerario.
        Assert.DoesNotContain(_persisted!.Items, i => i.ExperienceId == expensive.ExperienceId);
    }

    [Fact]
    public async Task PreferPackage_ClearsCurrentItemsAndDoesNotExcludeThem()
    {
        var conversation = ConversationReadyToSearch();
        var first = ExperienceItem(1);
        var second = ExperienceItem(2);
        SetupCurrentItinerary(conversation, first, second);
        SetupIntent(ModificationAction.PREFER_PACKAGE);

        var packageId = Guid.NewGuid();
        RetrievalQuery? query = null;
        ItineraryCompositionRequest? compositionRequest = null;
        _retrievalService.Setup(r => r.RetrieveAsync(It.IsAny<RetrievalQuery>(), It.IsAny<CancellationToken>()))
            .Callback<RetrievalQuery, CancellationToken>((q, _) => query = q)
            .ReturnsAsync(new RetrievalResult([], [new CandidatePackage(packageId, "Uyuni 3 días", "Uyuni", 300, "USD", 3, [], [], IsStrongFit: true)]));
        _aiModelClient.Setup(c => c.ComposeItineraryAsync(It.IsAny<ItineraryCompositionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ItineraryCompositionRequest, CancellationToken>((req, _) => compositionRequest = req)
            .ReturnsAsync(new ItineraryCompositionResult("Viaje", [new ComposedItem(1, "PACKAGE", packageId, null)], "Un paquete cubre todo."));

        await SendAsync(conversation, "Prefiero un paquete.");

        Assert.Empty(compositionRequest!.PreservedItems); // se rearma para que un Package pueda cubrir varios días
        // Las experiencias sueltas no se excluyen: pueden volver dentro de un paquete.
        Assert.Empty(query!.ExcludedProductIds);
        Assert.Equal(packageId, Assert.Single(_persisted!.Items).PackageId);
    }

    [Fact]
    public async Task InterpretationUnavailable_FallsBackToNewSearchWithoutTouchingExistingItinerary()
    {
        var conversation = ConversationReadyToSearch();
        SetupCurrentItinerary(conversation, ExperienceItem(1));

        _aiModelClient.Setup(c => c.InterpretModificationAsync(It.IsAny<ModificationInterpretationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiModelUnavailableException("Ollama caído"));

        var result = await SendAsync(conversation, "Cambiá algo.");

        // Se degrada a búsqueda nueva (camino Oleada 5); nunca se borra la propuesta existente por un
        // fallo del modelo.
        Assert.False(result.ClarificationNeeded);
        _itineraryRepository.Verify(r => r.AddAsync(It.IsAny<AiItinerary>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MultiCurrencyItinerary_KeepsOneSubtotalPerCurrency()
    {
        var conversation = ConversationReadyToSearch();
        var usd = ExperienceItem(1, price: 40, currency: "USD");
        var eur = ExperienceItem(2, price: 35, currency: "EUR");
        var drop = ExperienceItem(3);
        SetupCurrentItinerary(conversation, usd, eur, drop);
        SetupRevalidationValid(usd, eur);
        SetupIntent(ModificationAction.REMOVE, [drop.Id]);

        var result = await SendAsync(conversation, "Quitá lo del día 3.");

        // 2 viajeros: 2×40 USD y 2×35 EUR, sin ninguna suma cruzada entre monedas.
        Assert.NotNull(result.Itinerary);
        Assert.Equal(2, result.Itinerary!.Totals.Count);
        Assert.Equal(80, result.Itinerary.Totals.Single(t => t.Currency == "USD").Amount);
        Assert.Equal(70, result.Itinerary.Totals.Single(t => t.Currency == "EUR").Amount);
    }
}
