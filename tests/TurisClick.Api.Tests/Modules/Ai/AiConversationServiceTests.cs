using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Ai.Dtos;
using TurisClick.Api.Modules.Ai.Entities;
using TurisClick.Api.Modules.Ai.Repositories;
using TurisClick.Api.Modules.Ai.Services;
using TurisClick.Api.Modules.Categories.Entities;
using TurisClick.Api.Modules.Categories.Repositories;
using TurisClick.Api.Modules.Destinations.Entities;
using TurisClick.Api.Modules.Destinations.Repositories;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Shared.Exceptions;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Ai;

/// <summary>
/// UC-T-12/13/14 — foco en ownership, el flujo de clarification determinístico y la validación
/// anti-hallucination (sección 11 de la sesión). Usa mocks de IAiModelClient/IRetrievalService, así que
/// no depende de Ollama ni de Postgres real (los caminos de integración están en AiEndpointsTests).
/// </summary>
public class AiConversationServiceTests
{
    private readonly Mock<IAiConversationRepository> _conversationRepository = new();
    private readonly Mock<IAiItineraryRepository> _itineraryRepository = new();
    private readonly Mock<IDestinationRepository> _destinationRepository = new();
    private readonly Mock<ICategoryRepository> _categoryRepository = new();
    private readonly Mock<IAiModelClient> _aiModelClient = new();
    private readonly Mock<IRetrievalService> _retrievalService = new();
    private readonly Mock<ICurrentUserContext> _currentUser = new();
    private readonly AiConversationService _sut;

    private readonly Guid _touristId = Guid.NewGuid();

    public AiConversationServiceTests()
    {
        var options = new DbContextOptionsBuilder<TurisClickDbContext>().Options;
        var db = new Mock<TurisClickDbContext>(options);
        db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        // AddMessage pasa por db.Set<AiMessage>() (virtual, mockeable) en vez de la propiedad de
        // conveniencia db.AiMessages (no virtual — con un DbContext mockeado y sin proveedor real
        // configurado, esa propiedad ejecuta la implementación real y explota con NullReferenceException).
        db.Setup(d => d.Set<AiMessage>()).Returns(Mock.Of<DbSet<AiMessage>>());

        _currentUser.Setup(c => c.UserId).Returns(_touristId);
        _destinationRepository.Setup(r => r.ListAsync(null, DestinationType.CITY, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _categoryRepository.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        _sut = new AiConversationService(
            _conversationRepository.Object,
            _itineraryRepository.Object,
            _destinationRepository.Object,
            _categoryRepository.Object,
            _aiModelClient.Object,
            _retrievalService.Object,
            _currentUser.Object,
            Mock.Of<ILogger<AiConversationService>>(),
            db.Object);
    }

    private AiConversation OwnConversation() => new()
    {
        Id = Guid.NewGuid(),
        TouristId = _touristId,
        Status = AiConversationStatus.ACTIVE,
        Messages = [],
        Categories = []
    };

    [Fact]
    public async Task GetByIdAsync_NotFound_ThrowsNotFound()
    {
        var id = Guid.NewGuid();
        _conversationRepository.Setup(r => r.GetByIdForReadAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((AiConversation?)null);

        await Assert.ThrowsAsync<NotFoundAppException>(() => _sut.GetByIdAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task GetByIdAsync_BelongsToAnotherTourist_ThrowsForbidden()
    {
        var conversation = new AiConversation { Id = Guid.NewGuid(), TouristId = Guid.NewGuid() };
        _conversationRepository.Setup(r => r.GetByIdForReadAsync(conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);

        await Assert.ThrowsAsync<ForbiddenAppException>(() => _sut.GetByIdAsync(conversation.Id, CancellationToken.None));
    }

    [Fact]
    public async Task SendMessage_ConversationNotFound_ThrowsNotFound()
    {
        var id = Guid.NewGuid();
        _conversationRepository.Setup(r => r.GetByIdForUpdateAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((AiConversation?)null);

        await Assert.ThrowsAsync<NotFoundAppException>(() =>
            _sut.SendMessageAsync(id, new SendMessageRequest { Content = "Hola" }, CancellationToken.None));
    }

    [Fact]
    public async Task SendMessage_BelongsToAnotherTourist_ThrowsForbidden()
    {
        var conversation = new AiConversation { Id = Guid.NewGuid(), TouristId = Guid.NewGuid(), Messages = [], Categories = [] };
        _conversationRepository.Setup(r => r.GetByIdForUpdateAsync(conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            _sut.SendMessageAsync(conversation.Id, new SendMessageRequest { Content = "Hola" }, CancellationToken.None));

        _aiModelClient.Verify(c => c.ExtractPreferencesAsync(It.IsAny<PreferenceExtractionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendMessage_MissingRequiredFields_ReturnsClarificationNeeded_WithoutRetrieval()
    {
        var conversation = OwnConversation();
        _conversationRepository.Setup(r => r.GetByIdForUpdateAsync(conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);

        _aiModelClient.Setup(c => c.ExtractPreferencesAsync(It.IsAny<PreferenceExtractionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreferenceExtractionResult(null, [], null, null, null, null, null, null, false, null));
        _aiModelClient.Setup(c => c.GenerateClarificationReplyAsync(It.IsAny<ClarificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("¿Qué fechas y cuántas personas?");

        var result = await _sut.SendMessageAsync(conversation.Id, new SendMessageRequest { Content = "Quiero ir a Uyuni." }, CancellationToken.None);

        Assert.True(result.ClarificationNeeded);
        Assert.NotEmpty(result.MissingInformation);
        Assert.Null(result.Itinerary);
        _retrievalService.Verify(r => r.RetrieveAsync(It.IsAny<RetrievalQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendMessage_AiModelUnavailable_ReturnsGracefulResponse_DoesNotThrow()
    {
        var conversation = OwnConversation();
        _conversationRepository.Setup(r => r.GetByIdForUpdateAsync(conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);

        _aiModelClient.Setup(c => c.ExtractPreferencesAsync(It.IsAny<PreferenceExtractionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiModelUnavailableException("Ollama no responde"));

        var result = await _sut.SendMessageAsync(conversation.Id, new SendMessageRequest { Content = "Hola" }, CancellationToken.None);

        Assert.False(result.ClarificationNeeded);
        Assert.NotEmpty(result.Warnings);
        Assert.False(string.IsNullOrWhiteSpace(result.AssistantMessage));
    }

    [Fact]
    public async Task SendMessage_ComposedItemNotInCandidates_IsRejectedWithWarning()
    {
        var destinationId = Guid.NewGuid();
        var conversation = OwnConversation();
        conversation.PreferredDestinationId = destinationId;
        conversation.StartDate = new DateOnly(2026, 9, 10);
        conversation.EndDate = new DateOnly(2026, 9, 12);
        conversation.TravelersCount = 2;

        _conversationRepository.Setup(r => r.GetByIdForUpdateAsync(conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);

        _aiModelClient.Setup(c => c.ExtractPreferencesAsync(It.IsAny<PreferenceExtractionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreferenceExtractionResult(null, [], null, null, null, null, null, null, false, null));

        var realExperienceId = Guid.NewGuid();
        var realCandidate = new CandidateExperience(realExperienceId, "Tour real", "Uyuni", 20, "USD", [], null,
            [new CandidateAvailability(Guid.NewGuid(), new DateOnly(2026, 9, 10), 5)]);

        _retrievalService.Setup(r => r.RetrieveAsync(It.IsAny<RetrievalQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RetrievalResult([realCandidate], []));

        var hallucinatedId = Guid.NewGuid(); // nunca estuvo en los candidatos ofrecidos
        _aiModelClient.Setup(c => c.ComposeItineraryAsync(It.IsAny<ItineraryCompositionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItineraryCompositionResult(
                "Mi viaje",
                [
                    new ComposedItem(1, "EXPERIENCE", realExperienceId, null),
                    new ComposedItem(1, "EXPERIENCE", hallucinatedId, null) // inventado por el modelo
                ],
                "Elegí esta experiencia real."));

        AiItinerary? addedItinerary = null;
        _itineraryRepository
            .Setup(r => r.AddAsync(It.IsAny<AiItinerary>(), It.IsAny<CancellationToken>()))
            .Callback<AiItinerary, CancellationToken>((it, _) => addedItinerary = it)
            .Returns(Task.CompletedTask);
        _itineraryRepository
            .Setup(r => r.GetByIdForReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => addedItinerary);

        var result = await _sut.SendMessageAsync(conversation.Id, new SendMessageRequest { Content = "Uyuni, 3 días" }, CancellationToken.None);

        Assert.NotNull(addedItinerary);
        Assert.Single(addedItinerary!.Items); // el hallucinado se descartó, solo el real se persistió
        Assert.Equal(realExperienceId, addedItinerary.Items.Single().ExperienceId);
        Assert.Contains(result.Warnings, w => w.Contains("descartó", StringComparison.OrdinalIgnoreCase));
    }
}
