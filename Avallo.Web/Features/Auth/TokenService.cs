using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Avallo.Web.Domain;
using Avallo.Web.Infrastructure;

namespace Avallo.Web.Features.Auth;

public sealed class TokenService(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    IOptions<JwtOptions> options,
    TimeProvider timeProvider)
{
    public const string RefreshCookieName = "__Host-refresh-token";
    private readonly JwtOptions _options = options.Value;

    public async Task<(TokenResponse Response, string RefreshToken)> IssueAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var roles = await userManager.GetRolesAsync(user);
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("tenant_id", user.TenantId.ToString()),
            new("name", user.DisplayName),
            new("security_stamp", user.SecurityStamp ?? string.Empty),
            new("password_change_required", user.RequiresPasswordChange ? "true" : "false")
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)),
            SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            now.UtcDateTime,
            expiresAt.UtcDateTime,
            credentials);

        var rawRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        db.RefreshTokens.Add(new RefreshToken
        {
            TenantId = user.TenantId,
            UserId = user.Id,
            TokenHash = Hash(rawRefreshToken),
            ExpiresAt = now.AddDays(_options.RefreshTokenDays)
        });
        await db.SaveChangesAsync(cancellationToken);

        return (
            new TokenResponse(
                new JwtSecurityTokenHandler().WriteToken(jwt),
                expiresAt,
                new UserResponse(user.Id, user.TenantId, user.Email!, user.DisplayName, roles.ToArray(),
                    user.RequiresPasswordChange)),
            rawRefreshToken);
    }

    public async Task<(TokenResponse Response, string RefreshToken)?> RotateAsync(
        string rawToken,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var current = await db.RefreshTokens.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TokenHash == Hash(rawToken), cancellationToken);

        if (current is null)
            return null;

        if (!current.IsActive(now))
        {
            await db.RefreshTokens.IgnoreQueryFilters()
                .Where(x => x.UserId == current.UserId && x.RevokedAt == null && x.ExpiresAt > now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, now), cancellationToken);
            return null;
        }

        var user = await userManager.FindByIdAsync(current.UserId.ToString());
        if (user is null || !user.IsActive)
            return null;

        current.RevokedAt = now;
        var issued = await IssueAsync(user, cancellationToken);
        var replacementHash = Hash(issued.RefreshToken);
        current.ReplacedByTokenId = await db.RefreshTokens.IgnoreQueryFilters()
            .Where(x => x.TokenHash == replacementHash)
            .Select(x => x.Id)
            .SingleAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return issued;
    }

    public async Task RevokeAsync(string rawToken, CancellationToken cancellationToken)
    {
        var tokenHash = Hash(rawToken);
        await db.RefreshTokens.IgnoreQueryFilters()
            .Where(x => x.TokenHash == tokenHash && x.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.RevokedAt, timeProvider.GetUtcNow()),
                cancellationToken);
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
