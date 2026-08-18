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

/// <summary>UC-A-05 — unicidad de nombre.</summary>
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
    public async Task CreateAsync_NewName_Succeeds()
    {
        _repository.Setup(r => r.ExistsWithNameAsync("Aventura", null, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _sut.CreateAsync(new CreateCategoryRequest { Name = "Aventura" }, CancellationToken.None);

        Assert.Equal("Aventura", result.Name);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ThrowsConflict()
    {
        _repository.Setup(r => r.ExistsWithNameAsync("Aventura", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<ConflictAppException>(() =>
            _sut.CreateAsync(new CreateCategoryRequest { Name = "Aventura" }, CancellationToken.None));
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
    public async Task DeleteAsync_Existing_RemovesIt()
    {
        var id = Guid.NewGuid();
        var category = new Category { Id = id, Name = "Aventura" };
        _repository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(category);

        await _sut.DeleteAsync(id, CancellationToken.None);

        _repository.Verify(r => r.Remove(category), Times.Once);
    }
}
