using Microsoft.AspNetCore.Identity;
using TurisClick.Api.Modules.Auth.Entities;

namespace TurisClick.Api.Infrastructure.Security;

public interface IPasswordHasherService
{
    string Hash(string password);
    bool Verify(string passwordHash, string providedPassword);
}

/// <summary>
/// Envoltorio sobre PasswordHasher&lt;User&gt; de ASP.NET Core Identity (PBKDF2 con salt, HMAC-SHA256).
/// No se implementa hashing propio — ver docs/backend-architecture.md, punto 5.
/// </summary>
public class PasswordHasherService : IPasswordHasherService
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(user: null!, password);

    public bool Verify(string passwordHash, string providedPassword)
    {
        var result = _hasher.VerifyHashedPassword(user: null!, passwordHash, providedPassword);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
