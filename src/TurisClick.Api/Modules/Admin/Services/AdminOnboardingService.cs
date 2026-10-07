using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Admin.Dtos;
using TurisClick.Api.Modules.Auth.Entities;
using TurisClick.Api.Modules.Companies.Dtos;
using TurisClick.Api.Modules.Companies.Entities;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Admin.Services;

public interface IAdminOnboardingService
{
    Task<ProviderAccountCreatedResponse> CreateProviderAccountAsync(
        CreateProviderAccountRequest request, CancellationToken ct);

    Task<ResetProviderPasswordResponse> ResetProviderPasswordAsync(Guid userId, CancellationToken ct);

    Task<List<CompanyUserResponse>> ListCompanyUsersAsync(Guid companyId, CancellationToken ct);
}

/// <summary>
/// Alta de operadores por parte de un administrador.
///
/// **Reemplaza el autorregistro público**, y el cambio es de producto antes que técnico: en TurisClick una
/// empresa de turismo no se da de alta sola. Alguien de la plataforma la conoce, la carga y le entrega sus
/// credenciales. Dejar un endpoint anónimo que cree empresas y cuentas con rol PROVIDER era, además, una
/// escalada de privilegios servida: cualquiera podía obtener un rol con permiso de publicar.
///
/// Dos reglas que este servicio sostiene:
///
/// 1. **La contraseña temporal la genera el servidor y se muestra una sola vez.** No se guarda en claro, no
///    se manda por correo —no hay infraestructura de email y simularla sería peor— y no se puede volver a
///    consultar: si se pierde, se regenera.
/// 2. **La cuenta no puede operar hasta cambiarla.** El token lleva el claim y la autorización lo rechaza;
///    no es un aviso de pantalla.
/// </summary>
public class AdminOnboardingService(
    TurisClickDbContext db,
    IPasswordHasherService passwordHasher) : IAdminOnboardingService
{
    public async Task<ProviderAccountCreatedResponse> CreateProviderAccountAsync(
        CreateProviderAccountRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var legalDocument = request.LegalDocument.Trim();

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new ConflictAppException("Ya existe una cuenta con ese email.");

        if (await db.Companies.AnyAsync(c => c.LegalDocument == legalDocument, ct))
            throw new ConflictAppException("Ya existe una empresa con ese documento legal.");

        var now = DateTimeOffset.UtcNow;

        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = request.CompanyName.Trim(),
            Description = string.IsNullOrWhiteSpace(request.CompanyDescription) ? null : request.CompanyDescription.Trim(),
            LegalDocument = legalDocument,
            ContactEmail = request.ContactEmail.Trim().ToLowerInvariant(),
            ContactPhone = string.IsNullOrWhiteSpace(request.ContactPhone) ? null : request.ContactPhone.Trim(),
            // Quien da el alta ya conoce a la empresa; pedirle que después se la apruebe a sí mismo sería un
            // paso ceremonial. Puede quedar pendiente igual si así lo decide.
            Status = request.Approve ? CompanyStatus.APPROVED : CompanyStatus.PENDING_APPROVAL,
            CreatedAt = now,
        };

        if (request.Approve) company.ApprovedAt = now;

        var temporaryPassword = GenerateTemporaryPassword();

        var user = new User
        {
            Id = Guid.NewGuid(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = email,
            PasswordHash = passwordHasher.Hash(temporaryPassword),
            Role = UserRole.PROVIDER,
            Status = UserStatus.ACTIVE,
            CompanyId = company.Id,
            MustChangePassword = true,
            CreatedAt = now,
        };

        db.Companies.Add(company);
        db.Users.Add(user);

        // Empresa y cuenta se confirman juntas: una empresa sin cuenta no se puede operar, y una cuenta sin
        // empresa no puede publicar nada.
        await db.SaveChangesAsync(ct);

        return new ProviderAccountCreatedResponse
        {
            Company = new CompanySummaryResponse
            {
                Id = company.Id,
                Name = company.Name,
                Status = company.Status.ToString(),
            },
            UserId = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            TemporaryPassword = temporaryPassword,
        };
    }

    public async Task<ResetProviderPasswordResponse> ResetProviderPasswordAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundAppException("Cuenta no encontrada.");

        if (user.Role != UserRole.PROVIDER)
            throw new ValidationAppException("Sólo se puede regenerar la credencial de una cuenta de operador.");

        var temporaryPassword = GenerateTemporaryPassword();

        user.PasswordHash = passwordHasher.Hash(temporaryPassword);
        user.MustChangePassword = true;

        // Las sesiones abiertas con la contraseña vieja dejan de poder renovarse: regenerar una credencial sin
        // cortar lo que ya estaba andando no cambiaría nada para quien la tenga.
        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in tokens) token.RevokedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        return new ResetProviderPasswordResponse
        {
            UserId = user.Id,
            Email = user.Email,
            TemporaryPassword = temporaryPassword,
        };
    }

    public async Task<List<CompanyUserResponse>> ListCompanyUsersAsync(Guid companyId, CancellationToken ct) =>
        await db.Users
            .AsNoTracking()
            .Where(u => u.CompanyId == companyId)
            .OrderBy(u => u.CreatedAt)
            .Select(u => new CompanyUserResponse
            {
                Id = u.Id,
                FullName = u.FirstName + " " + u.LastName,
                Email = u.Email,
                Status = u.Status.ToString(),
                MustChangePassword = u.MustChangePassword,
                CreatedAt = u.CreatedAt,
            })
            .ToListAsync(ct);

    /// <summary>
    /// Contraseña temporal legible pero no adivinable: tres bloques separados por guiones, de un alfabeto sin
    /// caracteres que se confundan (ni O ni 0, ni l ni 1). Se dicta por teléfono sin pedir que se repita, y
    /// aun así tiene suficiente entropía para el rato que vive.
    /// </summary>
    private static string GenerateTemporaryPassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        var blocks = Enumerable.Range(0, 3).Select(_ => new string(
            Enumerable.Range(0, 4).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray()));

        // El sufijo asegura que cumpla cualquier regla de complejidad sin depender del azar.
        return string.Join('-', blocks) + "-a1";
    }
}
