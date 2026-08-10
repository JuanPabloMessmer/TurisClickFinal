using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Modules.Auth.Entities;

namespace TurisClick.Api.Infrastructure.Database;

/// <summary>
/// Un solo DbContext para todo el proyecto (ver docs/backend-architecture.md, punto 11).
/// Cada módulo aporta sus IEntityTypeConfiguration; este contexto solo las descubre y expone los DbSet.
/// </summary>
public class TurisClickDbContext(DbContextOptions<TurisClickDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TurisClickDbContext).Assembly);
    }
}
