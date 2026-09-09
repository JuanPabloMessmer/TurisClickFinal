using Microsoft.Extensions.Options;
using Moq;
using TurisClick.Api.Modules.Ai.Repositories;
using TurisClick.Api.Modules.Ai.Services;
using TurisClick.Api.Modules.Categories.Entities;
using TurisClick.Api.Modules.Destinations.Entities;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Packages.Entities;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Ai;

/// <summary>UC-AI-02/03 — retrieval DB-first y scoring determinístico de fit de Package (sin LLM involucrado).</summary>
public class RetrievalServiceTests
{
    private readonly Mock<IAiCatalogRepository> _catalogRepository = new();
    private readonly RetrievalService _sut;

    private readonly Category _aventura = new() { Id = Guid.NewGuid(), Name = "Aventura" };
    private readonly Category _naturaleza = new() { Id = Guid.NewGuid(), Name = "Naturaleza" };
    private readonly Destination _uyuni = new() { Id = Guid.NewGuid(), Name = "Uyuni", Type = DestinationType.CITY };

    public RetrievalServiceTests()
    {
        var options = Options.Create(new AiOptions { MaxCandidatesPerType = 8 });
        _sut = new RetrievalService(_catalogRepository.Object, options);
    }

    private Package MakePackage(int durationDays, decimal price, string currency, params Category[] categories) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Uyuni Package",
        Destination = _uyuni,
        DestinationId = _uyuni.Id,
        DurationDays = durationDays,
        Price = price,
        Currency = currency,
        Categories = categories,
        Availabilities = [new PackageAvailability { Id = Guid.NewGuid(), DepartureDate = new DateOnly(2026, 9, 10), TotalSlots = 10, ReservedSlots = 0 }]
    };

    [Fact]
    public async Task RetrieveAsync_PackageWithinDurationCategoriesAndBudget_IsStrongFit()
    {
        var package = MakePackage(3, 250, "USD", _aventura, _naturaleza);
        _catalogRepository.Setup(r => r.SearchCandidatePackagesAsync(It.IsAny<AiCatalogFilter>(), It.IsAny<CancellationToken>())).ReturnsAsync([package]);
        _catalogRepository.Setup(r => r.SearchCandidateExperiencesAsync(It.IsAny<AiCatalogFilter>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var query = new RetrievalQuery(_uyuni.Id, new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 14), 5, 300, "USD", [_aventura.Id], []);

        var result = await _sut.RetrieveAsync(query, CancellationToken.None);

        Assert.True(Assert.Single(result.Packages).IsStrongFit);
    }

    [Fact]
    public async Task RetrieveAsync_PackageTooLongForTrip_IsNotStrongFit()
    {
        // Paquete de 6 días para un viaje de 3 días no puede ser la base del itinerario.
        var package = MakePackage(6, 250, "USD", _aventura);
        _catalogRepository.Setup(r => r.SearchCandidatePackagesAsync(It.IsAny<AiCatalogFilter>(), It.IsAny<CancellationToken>())).ReturnsAsync([package]);
        _catalogRepository.Setup(r => r.SearchCandidateExperiencesAsync(It.IsAny<AiCatalogFilter>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var query = new RetrievalQuery(_uyuni.Id, null, null, 3, null, null, [], []);

        var result = await _sut.RetrieveAsync(query, CancellationToken.None);

        Assert.False(Assert.Single(result.Packages).IsStrongFit);
    }

    [Fact]
    public async Task RetrieveAsync_PackageOverBudget_IsNotStrongFit()
    {
        var package = MakePackage(3, 900, "USD", _aventura);
        _catalogRepository.Setup(r => r.SearchCandidatePackagesAsync(It.IsAny<AiCatalogFilter>(), It.IsAny<CancellationToken>())).ReturnsAsync([package]);
        _catalogRepository.Setup(r => r.SearchCandidateExperiencesAsync(It.IsAny<AiCatalogFilter>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var query = new RetrievalQuery(_uyuni.Id, null, null, 3, 500, "USD", [], []);

        var result = await _sut.RetrieveAsync(query, CancellationToken.None);

        Assert.False(Assert.Single(result.Packages).IsStrongFit);
    }

    [Fact]
    public async Task RetrieveAsync_DifferentCurrencyBudget_DoesNotFilterOutOrScoreByPrice()
    {
        // Sección 12 de la sesión: nunca se inventa conversión FX — un presupuesto en USD no debe
        // afectar el fit de un Package en BOB (ni a favor ni en contra por precio).
        var package = MakePackage(3, 900, "BOB", _aventura);
        _catalogRepository.Setup(r => r.SearchCandidatePackagesAsync(It.IsAny<AiCatalogFilter>(), It.IsAny<CancellationToken>())).ReturnsAsync([package]);
        _catalogRepository.Setup(r => r.SearchCandidateExperiencesAsync(It.IsAny<AiCatalogFilter>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var query = new RetrievalQuery(_uyuni.Id, null, null, 3, 500, "USD", [_aventura.Id], []);

        var result = await _sut.RetrieveAsync(query, CancellationToken.None);

        // Presupuesto no aplica (moneda distinta) => budgetOk se considera true (no bloquea el fit),
        // pero tampoco se usa el precio para nada — solo importan duración y categorías acá.
        Assert.True(Assert.Single(result.Packages).IsStrongFit);
    }

    [Fact]
    public async Task RetrieveAsync_LimitsToMaxCandidatesPerType()
    {
        var experiences = Enumerable.Range(0, 20)
            .Select(_ => new Experience
            {
                Id = Guid.NewGuid(),
                Title = "Tour",
                Destination = _uyuni,
                DestinationId = _uyuni.Id,
                Price = 20,
                Currency = "USD",
                Categories = [],
                Availabilities = []
            })
            .ToList();

        _catalogRepository.Setup(r => r.SearchCandidateExperiencesAsync(It.IsAny<AiCatalogFilter>(), It.IsAny<CancellationToken>())).ReturnsAsync(experiences);
        _catalogRepository.Setup(r => r.SearchCandidatePackagesAsync(It.IsAny<AiCatalogFilter>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await _sut.RetrieveAsync(new RetrievalQuery(_uyuni.Id, null, null, null, null, null, [], []), CancellationToken.None);

        Assert.Equal(8, result.Experiences.Count); // MaxCandidatesPerType configurado en el options de arriba
    }
}
