using Microsoft.EntityFrameworkCore;
using Moq;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Destinations.Entities;
using TurisClick.Api.Modules.Destinations.Repositories;
using TurisClick.Api.Modules.Destinations.Services;
using TurisClick.Api.Shared.Exceptions;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Destinations;

/// <summary>UC-A-04 — reglas de jerarquía Country → Region → City y unicidad por nivel.</summary>
public class DestinationServiceTests
{
    private readonly Mock<IDestinationRepository> _repository = new();
    private readonly DestinationService _sut;

    public DestinationServiceTests()
    {
        var options = new DbContextOptionsBuilder<TurisClickDbContext>().Options;
        var db = new Mock<TurisClickDbContext>(options);
        db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _sut = new DestinationService(_repository.Object, db.Object);
    }

    [Fact]
    public async Task CreateAsync_CountryWithoutParent_Succeeds()
    {
        _repository.Setup(r => r.ExistsWithNameAsync("Bolivia", null, DestinationType.COUNTRY, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _sut.CreateAsync(new CreateDestinationRequest { Name = "Bolivia", Type = "COUNTRY" }, CancellationToken.None);

        Assert.Equal("Bolivia", result.Name);
        Assert.Equal("COUNTRY", result.Type);
        Assert.Null(result.ParentId);
    }

    [Fact]
    public async Task CreateAsync_CountryWithParent_ThrowsValidation()
    {
        var countryId = Guid.NewGuid();
        _repository.Setup(r => r.GetByIdAsync(countryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Destination { Id = countryId, Name = "Bolivia", Type = DestinationType.COUNTRY });

        var request = new CreateDestinationRequest { Name = "Algo", Type = "COUNTRY", ParentId = countryId };

        await Assert.ThrowsAsync<ValidationAppException>(() => _sut.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_RegionWithCountryParent_Succeeds()
    {
        var countryId = Guid.NewGuid();
        var country = new Destination { Id = countryId, Name = "Bolivia", Type = DestinationType.COUNTRY };
        _repository.Setup(r => r.GetByIdAsync(countryId, It.IsAny<CancellationToken>())).ReturnsAsync(country);
        _repository.Setup(r => r.ExistsWithNameAsync("Potosí", countryId, DestinationType.REGION, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _sut.CreateAsync(
            new CreateDestinationRequest { Name = "Potosí", Type = "REGION", ParentId = countryId }, CancellationToken.None);

        Assert.Equal("REGION", result.Type);
        Assert.Equal(countryId, result.ParentId);
    }

    [Fact]
    public async Task CreateAsync_RegionWithoutParent_ThrowsValidation()
    {
        var request = new CreateDestinationRequest { Name = "Potosí", Type = "REGION" };

        await Assert.ThrowsAsync<ValidationAppException>(() => _sut.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_RegionWithNonCountryParent_ThrowsValidation()
    {
        var cityId = Guid.NewGuid();
        _repository.Setup(r => r.GetByIdAsync(cityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Destination { Id = cityId, Name = "Uyuni", Type = DestinationType.CITY });

        var request = new CreateDestinationRequest { Name = "Potosí", Type = "REGION", ParentId = cityId };

        await Assert.ThrowsAsync<ValidationAppException>(() => _sut.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_CityWithRegionParent_Succeeds()
    {
        var regionId = Guid.NewGuid();
        var region = new Destination { Id = regionId, Name = "Potosí", Type = DestinationType.REGION };
        _repository.Setup(r => r.GetByIdAsync(regionId, It.IsAny<CancellationToken>())).ReturnsAsync(region);
        _repository.Setup(r => r.ExistsWithNameAsync("Uyuni", regionId, DestinationType.CITY, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _sut.CreateAsync(
            new CreateDestinationRequest { Name = "Uyuni", Type = "CITY", ParentId = regionId }, CancellationToken.None);

        Assert.Equal("CITY", result.Type);
    }

    [Fact]
    public async Task CreateAsync_CityWithCountryParent_ThrowsValidation()
    {
        var countryId = Guid.NewGuid();
        _repository.Setup(r => r.GetByIdAsync(countryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Destination { Id = countryId, Name = "Bolivia", Type = DestinationType.COUNTRY });

        var request = new CreateDestinationRequest { Name = "Uyuni", Type = "CITY", ParentId = countryId };

        await Assert.ThrowsAsync<ValidationAppException>(() => _sut.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_ParentNotFound_ThrowsNotFound()
    {
        var missingParentId = Guid.NewGuid();
        _repository.Setup(r => r.GetByIdAsync(missingParentId, It.IsAny<CancellationToken>())).ReturnsAsync((Destination?)null);

        var request = new CreateDestinationRequest { Name = "Potosí", Type = "REGION", ParentId = missingParentId };

        await Assert.ThrowsAsync<NotFoundAppException>(() => _sut.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_DuplicateNameAtSameLevel_ThrowsConflict()
    {
        _repository.Setup(r => r.ExistsWithNameAsync("Bolivia", null, DestinationType.COUNTRY, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new CreateDestinationRequest { Name = "Bolivia", Type = "COUNTRY" };

        await Assert.ThrowsAsync<ConflictAppException>(() => _sut.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_WithChildren_ThrowsConflict()
    {
        var id = Guid.NewGuid();
        _repository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Destination { Id = id, Name = "Bolivia", Type = DestinationType.COUNTRY });
        _repository.Setup(r => r.HasChildrenAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<ConflictAppException>(() => _sut.DeleteAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_WithoutChildren_Succeeds()
    {
        var id = Guid.NewGuid();
        var destination = new Destination { Id = id, Name = "Uyuni", Type = DestinationType.CITY };
        _repository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(destination);
        _repository.Setup(r => r.HasChildrenAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await _sut.DeleteAsync(id, CancellationToken.None);

        _repository.Verify(r => r.Remove(destination), Times.Once);
    }
}
