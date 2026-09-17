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
using TurisClick.Api.Modules.Preferences.Entities;
using TurisClick.Api.Modules.Preferences.Services;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Ai;

/// <summary>
/// El perfil del onboarding como punto de partida del asistente: completa lo que la conversación no dice,
/// y lo que el turista pide en la conversación siempre tiene prioridad.
/// </summary>
public class AiConversationProfileTests
{
    private readonly Mock<IAiConversationRepository> _conversationRepository = new();
    private readonly Mock<IAiItineraryRepository> _itineraryRepository = new();
    private readonly Mock<IDestinationRepository> _destinationRepository = new();
    private readonly Mock<ICategoryRepository> _categoryRepository = new();
    private readonly Mock<IAiModelClient> _aiModelClient = new();
    private readonly Mock<IRetrievalService> _retrievalService = new();
    private readonly Mock<IItineraryRevalidationService> _revalidationService = new();
    private readonly Mock<ICurrentUserContext> _currentUser = new();
    private readonly Mock<ITouristPreferenceService> _preferences = new();
    private readonly AiConversationService _sut;

    private readonly Guid _touristId = Guid.NewGuid();
    private static readonly Category Nature = new() { Id = Guid.NewGuid(), Name = "Naturaleza" };
    private static readonly Category Adventure = new() { Id = Guid.NewGuid(), Name = "Aventura" };
    private static readonly Category Culture = new() { Id = Guid.NewGuid(), Name = "Cultura" };
    private static readonly Destination Sucre = new() { Id = Guid.NewGuid(), Name = "Sucre", Type = DestinationType.CITY };

    private RetrievalQuery? _lastQuery;
    private ItineraryCompositionRequest? _lastComposition;

    public AiConversationProfileTests()
    {
        var db = new Mock<TurisClickDbContext>(new DbContextOptionsBuilder<TurisClickDbContext>().Options);
        db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        db.Setup(d => d.Set<AiMessage>()).Returns(Mock.Of<DbSet<AiMessage>>());

        _currentUser.Setup(c => c.UserId).Returns(_touristId);
        _revalidationService.Setup(r => r.RevalidateAsync(It.IsAny<AiItinerary>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ItineraryRevalidationResult.Empty);
        _destinationRepository.Setup(r => r.ListAsync(null, DestinationType.CITY, It.IsAny<CancellationToken>())).ReturnsAsync([Sucre]);
        _categoryRepository.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Nature, Adventure, Culture]);
        _itineraryRepository.Setup(r => r.GetLatestByConversationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((AiItinerary?)null);

        var candidate = new CandidateExperience(Guid.NewGuid(), "Tour", "Sucre", 100, "BOB", [], 120,
            [new CandidateAvailability(Guid.NewGuid(), new DateOnly(2026, 10, 10), 5)]);
        _retrievalService.Setup(r => r.RetrieveAsync(It.IsAny<RetrievalQuery>(), It.IsAny<CancellationToken>()))
            .Callback<RetrievalQuery, CancellationToken>((q, _) => _lastQuery = q)
            .ReturnsAsync(new RetrievalResult([candidate], []));
        _aiModelClient.Setup(c => c.ComposeItineraryAsync(It.IsAny<ItineraryCompositionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ItineraryCompositionRequest, CancellationToken>((r, _) => _lastComposition = r)
            .ReturnsAsync(new ItineraryCompositionResult("Viaje", [new ComposedItem(1, "EXPERIENCE", candidate.Id, null)], "Listo."));
        _aiModelClient.Setup(c => c.GenerateClarificationReplyAsync(It.IsAny<ClarificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("¿Cuántos viajan?");

        AiItinerary? added = null;
        _itineraryRepository.Setup(r => r.AddAsync(It.IsAny<AiItinerary>(), It.IsAny<CancellationToken>()))
            .Callback<AiItinerary, CancellationToken>((it, _) => added = it).Returns(Task.CompletedTask);
        _itineraryRepository.Setup(r => r.GetByIdForReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => added);

        _sut = new AiConversationService(
            _conversationRepository.Object, _itineraryRepository.Object, _destinationRepository.Object, _categoryRepository.Object,
            _aiModelClient.Object, _retrievalService.Object, _revalidationService.Object, _currentUser.Object,
            Mock.Of<ILogger<AiConversationService>>(), db.Object, _preferences.Object);
    }

    private AiConversation Conversation()
    {
        var conversation = new AiConversation
        {
            Id = Guid.NewGuid(), TouristId = _touristId, Status = AiConversationStatus.ACTIVE,
            PreferredDestinationId = Sucre.Id, PreferredDestination = Sucre, DurationDays = 2, Messages = [], Categories = []
        };
        _conversationRepository.Setup(r => r.GetByIdForUpdateAsync(conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        return conversation;
    }

    private void Profile(TravelParty? party = null, TravelPace? pace = null, BudgetLevel? budget = null, params Category[] interests) =>
        _preferences.Setup(p => p.FindForUserAsync(_touristId, It.IsAny<CancellationToken>())).ReturnsAsync(new TouristPreference
        {
            UserId = _touristId, TravelParty = party, TravelPace = pace, BudgetLevel = budget, Categories = [.. interests]
        });

    private void Extraction(IReadOnlyList<string> categories, string? pace = null, int? travelers = null) =>
        _aiModelClient.Setup(c => c.ExtractPreferencesAsync(It.IsAny<PreferenceExtractionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreferenceExtractionResult(null, categories, null, null, null, travelers, null, null, false, null, pace));

    [Fact]
    public async Task ProfileFillsInterestsTravelersPaceAndBudget_WhenTheMessageDoesNotSayThem()
    {
        var conversation = Conversation();
        Profile(TravelParty.SOLO, TravelPace.INTENSE, BudgetLevel.ECONOMY, Nature, Adventure);
        Extraction([]);

        var result = await _sut.SendMessageAsync(conversation.Id, new SendMessageRequest { Content = "Armame algo" }, CancellationToken.None);

        Assert.False(result.ClarificationNeeded); // viajeros inferidos de "solo"
        Assert.NotNull(result.Itinerary);
        Assert.Equal(1, conversation.TravelersCount);
        Assert.Equal([Nature.Id, Adventure.Id], _lastQuery!.InterestCategoryIds.OrderBy(id => id == Adventure.Id));
        Assert.Equal(250m, _lastQuery.BudgetPerPerson);
        Assert.Equal("BOB", _lastQuery.BudgetCurrency);
        Assert.Equal("INTENSE", _lastComposition!.TravelPace);
        Assert.Contains(result.ProfileHints, h => h.Contains("Naturaleza") && h.Contains("Aventura"));
        Assert.Contains(result.ProfileHints, h => h.Contains("solo"));
        // Los intereses del perfil NO se copian a la conversación: siguen siendo del perfil.
        Assert.Empty(conversation.Categories);
    }

    [Fact]
    public async Task CurrentMessageOverridesProfile_InterestsAndPace()
    {
        var conversation = Conversation();
        Profile(TravelParty.SOLO, TravelPace.INTENSE, null, Nature, Adventure);
        Extraction(["Cultura"], pace: "RELAXED");

        var result = await _sut.SendMessageAsync(conversation.Id,
            new SendMessageRequest { Content = "Esta vez quiero algo tranquilo y cultural en Sucre." }, CancellationToken.None);

        Assert.Equal([Culture.Id], _lastQuery!.InterestCategoryIds);
        Assert.Equal("RELAXED", _lastComposition!.TravelPace);
        Assert.Equal(TravelPace.RELAXED, conversation.TravelPace);
        Assert.DoesNotContain(result.ProfileHints, h => h.Contains("intereses"));
        Assert.DoesNotContain(result.ProfileHints, h => h.Contains("Ritmo"));
    }

    [Fact]
    public async Task FriendsOrFamily_DoesNotGuessTravelers_AndStillAsks()
    {
        var conversation = Conversation();
        Profile(TravelParty.FAMILY, null, null, Nature);
        Extraction([]);

        var result = await _sut.SendMessageAsync(conversation.Id, new SendMessageRequest { Content = "Quiero ir" }, CancellationToken.None);

        Assert.True(result.ClarificationNeeded);
        Assert.Contains(result.MissingInformation, m => m.Contains("viajeros"));
        Assert.Null(conversation.TravelersCount);
        Assert.Contains(result.ProfileHints, h => h.Contains("Naturaleza"));
    }

    [Fact]
    public async Task ExplicitTravelersInConversation_WinOverProfileParty()
    {
        var conversation = Conversation();
        Profile(TravelParty.COUPLE, null, BudgetLevel.PREMIUM);
        Extraction([], travelers: 4);

        var result = await _sut.SendMessageAsync(conversation.Id, new SendMessageRequest { Content = "Somos 4 personas" }, CancellationToken.None);

        Assert.Equal(4, conversation.TravelersCount);
        Assert.Null(_lastQuery!.BudgetPerPerson); // premium = sin techo de precio
        Assert.Contains(result.ProfileHints, h => h.Contains("premium"));
    }

    [Fact]
    public async Task WithoutProfile_BehavesAsBefore()
    {
        var conversation = Conversation();
        conversation.TravelersCount = 2;
        _preferences.Setup(p => p.FindForUserAsync(_touristId, It.IsAny<CancellationToken>())).ReturnsAsync((TouristPreference?)null);
        Extraction([]);

        var result = await _sut.SendMessageAsync(conversation.Id, new SendMessageRequest { Content = "Dale" }, CancellationToken.None);

        Assert.Empty(result.ProfileHints);
        Assert.Empty(_lastQuery!.InterestCategoryIds);
        Assert.Null(_lastComposition!.TravelPace);
    }
}
