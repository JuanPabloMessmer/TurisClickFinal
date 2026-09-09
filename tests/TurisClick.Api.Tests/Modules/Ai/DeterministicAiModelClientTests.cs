using TurisClick.Api.Modules.Ai.Services;
using TurisClick.Api.Modules.Ai.Services.LlmClients;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Ai;

/// <summary>UC-AI-01 — extracción de preferencias 100% determinística (sin LLM real), usada por tests/Newman.</summary>
public class DeterministicAiModelClientTests
{
    private readonly DeterministicAiModelClient _sut = new();

    private static readonly ExtractedPreferencesSnapshot EmptySnapshot = new(null, null, null, null, null, null, null, [], null);

    private static readonly string[] KnownDestinations = ["Uyuni", "La Paz", "Sucre"];
    private static readonly string[] KnownCategories = ["Aventura", "Naturaleza", "Gastronomía", "Cultura"];

    private PreferenceExtractionRequest Request(string message) =>
        new([], message, EmptySnapshot, KnownDestinations, KnownCategories, new DateOnly(2026, 8, 30));

    [Fact]
    public async Task ExtractPreferences_RecognizesKnownDestination()
    {
        var result = await _sut.ExtractPreferencesAsync(Request("Queremos conocer Uyuni con mi novia."), CancellationToken.None);

        Assert.Equal("Uyuni", result.DestinationMention);
    }

    [Fact]
    public async Task ExtractPreferences_UnknownDestination_ReturnsNull()
    {
        var result = await _sut.ExtractPreferencesAsync(Request("Queremos ir a Machu Picchu."), CancellationToken.None);

        Assert.Null(result.DestinationMention);
    }

    [Fact]
    public async Task ExtractPreferences_RecognizesKnownCategories()
    {
        var result = await _sut.ExtractPreferencesAsync(
            Request("Nos gusta la naturaleza, la gastronomía y la aventura."), CancellationToken.None);

        Assert.Contains("Naturaleza", result.CategoryMentions);
        Assert.Contains("Gastronomía", result.CategoryMentions);
        Assert.Contains("Aventura", result.CategoryMentions);
        Assert.DoesNotContain("Cultura", result.CategoryMentions);
    }

    [Fact]
    public async Task ExtractPreferences_ParsesIsoDateRange()
    {
        var result = await _sut.ExtractPreferencesAsync(Request("Del 2026-09-10 al 2026-09-15."), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 9, 10), result.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 15), result.EndDate);
    }

    [Fact]
    public async Task ExtractPreferences_ParsesTravelersFromExplicitNumber()
    {
        var result = await _sut.ExtractPreferencesAsync(Request("Somos 3 personas."), CancellationToken.None);

        Assert.Equal(3, result.TravelersCount);
    }

    [Fact]
    public async Task ExtractPreferences_ParsesTravelersFromCoupleMention()
    {
        var result = await _sut.ExtractPreferencesAsync(Request("Voy con mi novia."), CancellationToken.None);

        Assert.Equal(2, result.TravelersCount);
    }

    [Fact]
    public async Task ExtractPreferences_ParsesDurationDays()
    {
        var result = await _sut.ExtractPreferencesAsync(Request("Queremos un viaje de 5 días."), CancellationToken.None);

        Assert.Equal(5, result.DurationDays);
    }

    [Fact]
    public async Task ExtractPreferences_ParsesBudgetWithDollarSign_AsPerPerson()
    {
        var result = await _sut.ExtractPreferencesAsync(Request("Tenemos unos $500 por persona."), CancellationToken.None);

        Assert.Equal(500, result.BudgetAmount);
        Assert.Equal("USD", result.BudgetCurrency);
        Assert.True(result.BudgetIsPerPerson);
    }

    [Fact]
    public async Task ExtractPreferences_ParsesBudgetWithCurrencyWord_NotPerPerson()
    {
        var result = await _sut.ExtractPreferencesAsync(Request("Nuestro presupuesto total es de 1200 bolivianos."), CancellationToken.None);

        Assert.Equal(1200, result.BudgetAmount);
        Assert.Equal("BOB", result.BudgetCurrency);
        Assert.False(result.BudgetIsPerPerson);
    }

    [Fact]
    public async Task ExtractPreferences_PlainNumberWithoutCurrencyContext_IsNotTreatedAsBudget()
    {
        // "5 días" y "3 personas" no deberían confundirse con un monto de presupuesto.
        var result = await _sut.ExtractPreferencesAsync(Request("Vamos 3 personas por 5 días."), CancellationToken.None);

        Assert.Null(result.BudgetAmount);
    }

    [Fact]
    public async Task GenerateClarificationReply_MentionsOnlyMissingFields()
    {
        var reply = await _sut.GenerateClarificationReplyAsync(
            new ClarificationRequest([], "Quiero ir a Uyuni.", ["fechas o duración del viaje", "cantidad de viajeros"]), CancellationToken.None);

        Assert.Contains("fechas o duración del viaje", reply);
        Assert.Contains("cantidad de viajeros", reply);
    }

    [Fact]
    public async Task ComposeItinerary_PrefersStrongFitPackageAsBase()
    {
        var packageId = Guid.NewGuid();
        var experienceId = Guid.NewGuid();
        var request = new ItineraryCompositionRequest(
            EmptySnapshot,
            TripDurationDays: 5,
            CandidateExperiences: [new CandidateExperience(experienceId, "Cementerio de Trenes", "Uyuni", 20, "USD", [], null, [])],
            CandidatePackages: [new CandidatePackage(packageId, "Uyuni 3 días", "Uyuni", 300, "USD", 3, [], [], IsStrongFit: true)],
            PreservedItems: [],
            ModificationInstruction: null);

        var result = await _sut.ComposeItineraryAsync(request, CancellationToken.None);

        var packageItem = Assert.Single(result.Items, i => i.ProductType == "PACKAGE");
        Assert.Equal(packageId, packageItem.ProductId);
        Assert.Equal(1, packageItem.DayNumber);
        // El paquete ocupa los días 1-3; la experiencia debería completar a partir del día 4.
        var experienceItem = Assert.Single(result.Items, i => i.ProductType == "EXPERIENCE");
        Assert.Equal(4, experienceItem.DayNumber);
    }

    // ---- UC-AI-05: interpretación de ajustes sobre la propuesta vigente (Oleada 6) ----

    private static readonly Guid RaftingItemId = Guid.NewGuid();
    private static readonly Guid CenaItemId = Guid.NewGuid();

    private static readonly CurrentItineraryItemView[] CurrentItems =
    [
        new(RaftingItemId, 1, "EXPERIENCE", "Rafting", ["Aventura"], 60, "USD", new DateOnly(2026, 10, 5)),
        new(CenaItemId, 2, "EXPERIENCE", "Cena paceña", ["Gastronomía"], 30, "USD", new DateOnly(2026, 10, 6))
    ];

    private static ModificationInterpretationRequest Modification(string message, params CurrentItineraryItemView[] items) =>
        new([], message, EmptySnapshot, items.Length == 0 ? CurrentItems : items, KnownCategories);

    [Fact]
    public async Task InterpretModification_RemoveByCategory_TargetsThatItemOnly()
    {
        var result = await _sut.InterpretModificationAsync(Modification("Quitá la Gastronomía del viaje."), CancellationToken.None);

        Assert.Equal(ModificationAction.REMOVE, result.Action);
        Assert.Equal(CenaItemId, Assert.Single(result.TargetItemIds));
    }

    [Fact]
    public async Task InterpretModification_RemoveByProductName_TargetsThatItemOnly()
    {
        var result = await _sut.InterpretModificationAsync(Modification("No quiero Rafting."), CancellationToken.None);

        Assert.Equal(ModificationAction.REMOVE, result.Action);
        Assert.Equal(RaftingItemId, Assert.Single(result.TargetItemIds));
    }

    [Fact]
    public async Task InterpretModification_ChangeSpecificDay_TargetsOnlyThatDay()
    {
        var result = await _sut.InterpretModificationAsync(Modification("Cambiá el día 2."), CancellationToken.None);

        Assert.Equal(ModificationAction.REPLACE, result.Action);
        Assert.Equal(CenaItemId, Assert.Single(result.TargetItemIds));
        Assert.Equal(2, Assert.Single(result.TargetDays));
    }

    [Fact]
    public async Task InterpretModification_AddCategory_DoesNotTargetExistingItems()
    {
        var result = await _sut.InterpretModificationAsync(Modification("Agregá algo más de Aventura."), CancellationToken.None);

        Assert.Equal(ModificationAction.ADD, result.Action);
        Assert.Empty(result.TargetItemIds); // agregar no saca nada de lo que ya hay
        Assert.Contains("Aventura", result.AddCategoryNames);
    }

    [Fact]
    public async Task InterpretModification_LastDay_ResolvesAgainstRealItinerary()
    {
        var result = await _sut.InterpretModificationAsync(Modification("Agregá algo para el último día."), CancellationToken.None);

        Assert.Equal(ModificationAction.ADD, result.Action);
        Assert.Equal(2, Assert.Single(result.TargetDays)); // el último día real del itinerario, no un número inventado
    }

    [Fact]
    public async Task InterpretModification_SpendLess_IsReduceBudget()
    {
        var result = await _sut.InterpretModificationAsync(Modification("Quiero gastar menos."), CancellationToken.None);

        Assert.Equal(ModificationAction.REDUCE_BUDGET, result.Action);
    }

    [Fact]
    public async Task InterpretModification_PreferPackage_IsPreferPackage()
    {
        var result = await _sut.InterpretModificationAsync(Modification("Prefiero un paquete en vez de tantas experiencias."), CancellationToken.None);

        Assert.Equal(ModificationAction.PREFER_PACKAGE, result.Action);
    }

    [Fact]
    public async Task InterpretModification_UnrelatedMessage_IsNone()
    {
        var result = await _sut.InterpretModificationAsync(Modification("Gracias, muy bueno."), CancellationToken.None);

        Assert.Equal(ModificationAction.NONE, result.Action);
    }

    [Fact]
    public async Task InterpretModification_WithoutCurrentItinerary_IsNone()
    {
        var request = new ModificationInterpretationRequest([], "Quitá el rafting.", EmptySnapshot, [], KnownCategories);

        var result = await _sut.InterpretModificationAsync(request, CancellationToken.None);

        Assert.Equal(ModificationAction.NONE, result.Action);
    }

    [Fact]
    public async Task ComposeItinerary_WithPreservedItems_DoesNotReuseTheirDays()
    {
        // Sección 2: si el turista pidió cambiar solo un día, el resto no se regenera ni se pisa.
        var request = new ItineraryCompositionRequest(
            EmptySnapshot,
            TripDurationDays: 3,
            CandidateExperiences: [new CandidateExperience(Guid.NewGuid(), "Museo", "La Paz", 15, "USD", [], null, [])],
            CandidatePackages: [],
            PreservedItems: [new PreservedItem(1, "EXPERIENCE", Guid.NewGuid(), "Rafting")],
            ModificationInstruction: "Cambiá el día 2.");

        var result = await _sut.ComposeItineraryAsync(request, CancellationToken.None);

        Assert.Equal(2, Assert.Single(result.Items).DayNumber); // el día 1 sigue siendo del ítem preservado
    }

    [Fact]
    public async Task ComposeItinerary_WithPreservedItems_DoesNotProposeAPackageOverThem()
    {
        // Un Package cubre varios días: meterlo encima de lo preservado pisaría lo que el turista quiso conservar.
        var request = new ItineraryCompositionRequest(
            EmptySnapshot,
            TripDurationDays: 5,
            CandidateExperiences: [],
            CandidatePackages: [new CandidatePackage(Guid.NewGuid(), "Uyuni 3 días", "Uyuni", 300, "USD", 3, [], [], IsStrongFit: true)],
            PreservedItems: [new PreservedItem(1, "EXPERIENCE", Guid.NewGuid(), "Rafting")],
            ModificationInstruction: "Agregá algo para el día 2.");

        var result = await _sut.ComposeItineraryAsync(request, CancellationToken.None);

        Assert.Empty(result.Items);
    }

    // ---- UC-AI-06: explicación por componente ----

    [Fact]
    public async Task GenerateItemExplanation_UsesOnlyBackendFacts()
    {
        var item = CurrentItems[0];
        var facts = new[] { "Coincide con los intereses que indicaste: Aventura.", "Quedan 8 cupo(s) para esa fecha." };

        var result = await _sut.GenerateItemExplanationAsync(new ItemExplanationRequest(EmptySnapshot, item, facts), CancellationToken.None);

        Assert.Contains("Rafting", result);
        Assert.All(facts, fact => Assert.Contains(fact, result));
    }

    [Fact]
    public async Task GenerateItemExplanation_WithoutFacts_DoesNotInventAttributes()
    {
        var result = await _sut.GenerateItemExplanationAsync(
            new ItemExplanationRequest(EmptySnapshot, CurrentItems[0], []), CancellationToken.None);

        // Sección 5: sin datos reales no se inventa nada ("el favorito de los turistas" y similares).
        Assert.DoesNotContain("favorito", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("popular", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("USD", result); // no aparece precio: no se lo pasamos en los hechos
        Assert.Contains("Rafting", result);
    }
}
