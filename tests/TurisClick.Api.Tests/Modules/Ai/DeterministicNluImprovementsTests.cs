using TurisClick.Api.Modules.Ai.Services;
using TurisClick.Api.Modules.Ai.Services.LlmClients;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Ai;

/// <summary>NLU por reglas del proveedor Deterministic (el que corre en Azure): frases reales del asistente mobile.</summary>
public class DeterministicNluImprovementsTests
{
    private readonly DeterministicAiModelClient _sut = new();
    private static readonly ExtractedPreferencesSnapshot Empty = new(null, null, null, null, null, null, null, [], null);
    private static readonly string[] Destinations = ["La Paz", "Potosí", "Santa Cruz de la Sierra", "Sucre"];
    private static readonly string[] Categories = ["Naturaleza", "Gastronomía", "Cultura", "Aventura", "Historia"];

    // 2026-09-16 es miércoles.
    private Task<PreferenceExtractionResult> Extract(string message) =>
        _sut.ExtractPreferencesAsync(new PreferenceExtractionRequest([], message, Empty, Destinations, Categories, new DateOnly(2026, 9, 16)), CancellationToken.None);

    [Fact]
    public async Task IgnoresAccentsAndCase()
    {
        var result = await Extract("quiero ir a potosi, me gusta la gastronomia");

        Assert.Equal("Potosí", result.DestinationMention);
        Assert.Contains("Gastronomía", result.CategoryMentions);
    }

    [Fact]
    public async Task PrefersTheLongestDestinationName_AndStemsCategories()
    {
        var result = await Extract("Algo cultural e histórico en Santa Cruz de la Sierra");

        Assert.Equal("Santa Cruz de la Sierra", result.DestinationMention);
        Assert.Contains("Cultura", result.CategoryMentions);
        Assert.Contains("Historia", result.CategoryMentions);
    }

    [Fact]
    public async Task FourDaysInLaPaz_WithInterests()
    {
        var result = await Extract("Voy 4 días a La Paz. Me gusta la naturaleza y la gastronomía.");

        Assert.Equal("La Paz", result.DestinationMention);
        Assert.Equal(4, result.DurationDays);
        Assert.Equal(["Naturaleza", "Gastronomía"], result.CategoryMentions);
    }

    [Fact]
    public async Task ThisWeekend_ResolvesToNextSaturdayAndSunday()
    {
        var result = await Extract("Armame un viaje para este fin de semana");

        Assert.Equal(new DateOnly(2026, 9, 19), result.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 20), result.EndDate);
        Assert.Equal(2, result.DurationDays);
    }

    [Theory]
    [InlineData("Esta vez quiero algo tranquilo y cultural en Sucre.", "RELAXED")]
    [InlineData("Quiero aprovechar el día al máximo", "INTENSE")]
    [InlineData("un ritmo equilibrado", "BALANCED")]
    [InlineData("Voy a Sucre 3 días", null)]
    public async Task DetectsPace(string message, string? expected)
    {
        Assert.Equal(expected, (await Extract(message)).TravelPaceMention);
    }

    [Fact]
    public async Task TravelingAlone_MeansOneTraveler()
    {
        Assert.Equal(1, (await Extract("Viajo sola a Sucre")).TravelersCount);
        Assert.Null((await Extract("Solo quiero ver museos")).TravelersCount);
    }

    private static CurrentItineraryItemView Item(int day, string title, params string[] categories) =>
        new(Guid.NewGuid(), day, "EXPERIENCE", title, categories, 100, "BOB", null);

    [Fact]
    public async Task SecondDay_TargetsDayTwo()
    {
        var items = new[] { Item(1, "Tour A", "Cultura"), Item(2, "Tour B", "Naturaleza") };

        var intent = await _sut.InterpretModificationAsync(
            new ModificationInterpretationRequest([], "Cambiá el segundo día", Empty, items, Categories), CancellationToken.None);

        Assert.Equal(ModificationAction.REPLACE, intent.Action);
        Assert.Equal([items[1].ItemId], intent.TargetItemIds);
    }

    [Fact]
    public async Task LessAdventure_RemovesAdventureItems()
    {
        var items = new[] { Item(1, "Rafting", "Aventura"), Item(2, "Museo", "Historia") };

        var intent = await _sut.InterpretModificationAsync(
            new ModificationInterpretationRequest([], "Menos aventura", Empty, items, Categories), CancellationToken.None);

        Assert.Equal(ModificationAction.REMOVE, intent.Action);
        Assert.Equal([items[0].ItemId], intent.TargetItemIds);
    }

    [Fact]
    public async Task IntensePace_ComposesTwoActivitiesPerDay()
    {
        var candidates = Enumerable.Range(0, 4)
            .Select(i => new CandidateExperience(Guid.NewGuid(), $"T{i}", "Sucre", 50, "BOB", [], 60, []))
            .ToList();

        var relaxed = await _sut.ComposeItineraryAsync(new ItineraryCompositionRequest(Empty, 2, candidates, [], [], null, "RELAXED"), CancellationToken.None);
        var intense = await _sut.ComposeItineraryAsync(new ItineraryCompositionRequest(Empty, 2, candidates, [], [], null, "INTENSE"), CancellationToken.None);

        Assert.Equal(2, relaxed.Items.Count);
        Assert.Equal(4, intense.Items.Count);
        Assert.Equal([1, 1, 2, 2], intense.Items.Select(i => i.DayNumber));
    }
}
