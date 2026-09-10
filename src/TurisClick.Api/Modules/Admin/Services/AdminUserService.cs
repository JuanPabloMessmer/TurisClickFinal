using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Admin.Dtos;
using TurisClick.Api.Modules.Auth.Entities;
using TurisClick.Api.Shared.Exceptions;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Admin.Services;

public class AdminUserService(
    ICurrentUserContext currentUser,
    ILogger<AdminUserService> logger,
    TurisClickDbContext db) : IAdminUserService
{
    public async Task<PagedResult<AdminUserResponse>> ListAsync(
        UserRole? role, UserStatus? status, string? search, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Users.AsNoTracking();

        if (role.HasValue) query = query.Where(u => u.Role == role.Value);
        if (status.HasValue) query = query.Where(u => u.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(u =>
                EF.Functions.ILike(u.Email, term)
                || EF.Functions.ILike(u.FirstName, term)
                || EF.Functions.ILike(u.LastName, term));
        }

        var totalCount = await query.CountAsync(ct);

        var users = await query
            .Include(u => u.Company)
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<AdminUserResponse>
        {
            Items = users.Select(ToResponse).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public Task<AdminUserResponse> SuspendAsync(Guid userId, CancellationToken ct) =>
        ChangeStatusAsync(userId, UserStatus.SUSPENDED, ct);

    public Task<AdminUserResponse> ActivateAsync(Guid userId, CancellationToken ct) =>
        ChangeStatusAsync(userId, UserStatus.ACTIVE, ct);

    private async Task<AdminUserResponse> ChangeStatusAsync(Guid userId, UserStatus target, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.Company).FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundAppException("Usuario no encontrado.");

        // Un admin no puede dejarse a sí mismo fuera del sistema por accidente.
        if (target == UserStatus.SUSPENDED && user.Id == currentUser.UserId)
            throw new ConflictAppException("No podés suspender tu propia cuenta.");

        if (user.Status != target)
        {
            user.Status = target;
            await db.SaveChangesAsync(ct);

            logger.LogInformation("El admin {AdminId} cambió el estado del usuario {UserId} a {Status}.",
                currentUser.UserId, userId, target);
        }

        return ToResponse(user);
    }

    private static AdminUserResponse ToResponse(User user) => new()
    {
        Id = user.Id,
        FirstName = user.FirstName,
        LastName = user.LastName,
        Email = user.Email,
        Role = user.Role.ToString(),
        Status = user.Status.ToString(),
        CompanyId = user.CompanyId,
        CompanyName = user.Company?.Name,
        CreatedAt = user.CreatedAt
    };
}
