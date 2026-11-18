using Microsoft.EntityFrameworkCore;
using Moq;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Categories.Dtos;
using TurisClick.Api.Modules.Categories.Entities;
using TurisClick.Api.Modules.Categories.Repositories;
using TurisClick.Api.Modules.Categories.Services;
using TurisClick.Api.Shared.Exceptions;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Categories;

/// <summary>UC-A-05 — unicidad de nombre normalizado (R-07) y borrado bloqueado si está en uso (R-09).</summary>
public class CategoryServiceTests
{
    private readonly Mock<ICategoryRepository> _repository = new();
    private readonly CategoryService _sut;

    public CategoryServiceTests()
    {
        var options = new DbContextOptionsBuilder<TurisClickDbContext>().Options;
        var db = new Mock<TurisClickDbContext>(options);
        db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _sut = new CategoryService(_repository.Object, db.Object);
    }

    [Fact]
    public async Task CreateAsync_NewName_SavesTheNormalizedName()
    {
        _repository.Setup(r => r.FindNameConflictAsync("turismo 4x4", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await _sut.CreateAsync(new CreateCategoryRequest { Name = "  Turismo   4x4 " }, CancellationToken.None);

        Assert.Equal("Turismo 4x4", result.Name);
        Assert.Null(result.Description);
    }

    [Theory]
    [InlineData("aventura")]
    [InlineData("AVENTÚRA")]
    [InlineData("  Aventura  ")]
    public async Task CreateAsync_SameNormalizedName_ThrowsConflictOnTheNameField(string name)
    {
        _repository.Setup(r => r.FindNameConflictAsync("aventura", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Aventura");

        var ex = await Assert.ThrowsAsync<ConflictAppException>(() =>
            _sut.CreateAsync(new CreateCategoryRequest { Name = name }, CancellationToken.None));

        Assert.Equal("name", ex.Field);
        Assert.Equal(ErrorCodes.Duplicate, ex.ErrorCode);
        Assert.Contains("«Aventura»", ex.Message);
    }

    [Fact]
    public async Task UpdateAsync_ExcludesItselfFromTheUniquenessCheck()
    {
        var id = Guid.NewGuid();
        _repository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Category { Id = id, Name = "aventura" });
        _repository.Setup(r => r.FindNameConflictAsync("aventura", id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await _sut.UpdateAsync(id, new UpdateCategoryRequest { Name = "Aventura" }, CancellationToken.None);

        Assert.Equal("Aventura", result.Name);
    }

    [Fact]
    public async Task UpdateAsync_NotFound_ThrowsNotFound()
    {
        var id = Guid.NewGuid();
        _repository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((Category?)null);

        await Assert.ThrowsAsync<NotFoundAppException>(() =>
            _sut.UpdateAsync(id, new UpdateCategoryRequest { Name = "Nuevo" }, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_Unused_RemovesIt()
    {
        var id = Guid.NewGuid();
        var category = new Category { Id = id, Name = "Aventura" };
        _repository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(category);
        _repository.Setup(r => r.GetUsageAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(new CategoryUsage(0, 0, 0));

        await _sut.DeleteAsync(id, CancellationToken.None);

        _repository.Verify(r => r.Remove(category), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_InUse_ThrowsConflictAndDoesNotRemove()
    {
        var id = Guid.NewGuid();
        var category = new Category { Id = id, Name = "Aventura" };
        _repository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(category);
        _repository.Setup(r => r.GetUsageAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(new CategoryUsage(4, 1, 12));

        var ex = await Assert.ThrowsAsync<ConflictAppException>(() => _sut.DeleteAsync(id, CancellationToken.None));

        Assert.Equal(ErrorCodes.CategoryInUse, ex.ErrorCode);
        Assert.Equal(
            "No se puede eliminar «Aventura» porque está asignada a 4 experiencias y 1 paquete, y 12 turistas la eligieron como interés. Quitala de esos elementos antes de eliminarla.",
            ex.Message);
        _repository.Verify(r => r.Remove(It.IsAny<Category>()), Times.Never);
    }

    [Theory]
    [InlineData(1, 0, 0, "porque está asignada a 1 experiencia.")]
    [InlineData(0, 2, 0, "porque está asignada a 2 paquetes.")]
    [InlineData(0, 0, 1, "porque 1 turista la eligió como interés.")]
    public void InUseMessage_DescribesOnlyWhatUsesIt(int experiences, int packages, int tourists, string expected)
    {
        var message = CategoryService.InUseMessage("Cultura", new CategoryUsage(experiences, packages, tourists));

        Assert.Contains(expected, message);
    }
}
