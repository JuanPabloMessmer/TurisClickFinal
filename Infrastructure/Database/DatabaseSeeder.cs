using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Modules.Users;

namespace TurisClick.Api.Infrastructure.Database;

public static class DatabaseSeeder
{
    public static async Task SeedTemporaryAdminAsync(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger logger)
    {
        var seedSection = configuration.GetSection("TemporaryAdmin");

        if (!seedSection.GetValue<bool>("Enabled"))
        {
            return;
        }

        var email = seedSection["Email"]?.Trim().ToLowerInvariant();
        var password = seedSection["Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Temporary admin credentials are not configured.");
        }

        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (await dbContext.Users.AnyAsync(user => user.Email == email))
        {
            return;
        }

        var adminRole = await dbContext.Roles
            .FirstOrDefaultAsync(role => role.Name == "ADMIN")
            ?? throw new InvalidOperationException("ADMIN role was not found.");

        var user = new User
        {
            RoleId = adminRole.RoleId,
            FirstName = seedSection["FirstName"]?.Trim() ?? "Temporary",
            LastName = string.IsNullOrWhiteSpace(seedSection["LastName"])
                ? null
                : seedSection["LastName"]!.Trim(),
            Email = email,
            Status = "ACTIVE",
            CreatedAt = DateTime.UtcNow,
            Role = adminRole
        };

        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        logger.LogWarning(
            "Temporary admin user {Email} was created. Disable the seed and rotate the password before production.",
            email);
    }
}
