using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Infrastructure;

namespace Avallo.Web.Features.Auth;

public sealed class UserAccessService(AppDbContext db, TimeProvider timeProvider)
{
    public async Task<TenantUserResponse[]> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var rows = await (
            from user in db.Users.AsNoTracking()
            join userRole in db.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
            join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where user.TenantId == tenantId
            orderby role.Name, user.DisplayName
            select new { user.Id, user.Email, user.DisplayName, Role = role.Name!, user.IsActive,
                user.RequiresPasswordChange, user.CreatedAt }).ToArrayAsync(cancellationToken);
        return rows.Select(x => new TenantUserResponse(x.Id, x.Email!, x.DisplayName, x.Role,
            x.IsActive, x.RequiresPasswordChange, x.CreatedAt)).ToArray();
    }

    public async Task<TenantUserResponse> SetActiveAsync(Guid tenantId, Guid actorUserId, Guid targetUserId,
        bool isActive, CancellationToken cancellationToken = default)
    {
        if (targetUserId == actorUserId)
            throw new InvalidOperationException("Voce nao pode alterar o status do proprio acesso.");
        var user = await db.Users.SingleOrDefaultAsync(
            x => x.Id == targetUserId && x.TenantId == tenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Usuario nao encontrado neste tenant.");
        var role = await (
            from userRole in db.UserRoles
            join identityRole in db.Roles on userRole.RoleId equals identityRole.Id
            where userRole.UserId == user.Id
            select identityRole.Name!).SingleAsync(cancellationToken);
        if (role == Roles.Admin)
            throw new InvalidOperationException("Acessos administrativos nao podem ser alterados por esta tela.");
        user.IsActive = isActive;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        if (!isActive)
        {
            var now = timeProvider.GetUtcNow();
            var refreshTokens = await db.RefreshTokens
                .Where(x => x.UserId == user.Id && x.RevokedAt == null && x.ExpiresAt > now)
                .ToArrayAsync(cancellationToken);
            foreach (var token in refreshTokens)
                token.RevokedAt = now;
        }
        await db.SaveChangesAsync(cancellationToken);
        return new TenantUserResponse(user.Id, user.Email!, user.DisplayName, role,
            user.IsActive, user.RequiresPasswordChange, user.CreatedAt);
    }
}
