using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Features.Auth;
using Avallo.Web.Infrastructure;
using Xunit;

namespace Avallo.Tests.Features;

public sealed class UserAccessServiceTests
{
    [Fact]
    public async Task List_returns_only_users_from_admin_tenant()
    {
        var fixture = CreateFixture();
        var own = AddUser(fixture, fixture.TenantId, Roles.Accountant, "contador@empresa.test");
        AddUser(fixture, Guid.NewGuid(), Roles.Accountant, "outro@empresa.test");
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var users = await fixture.Service.ListAsync(fixture.TenantId, TestContext.Current.CancellationToken);

        var listed = Assert.Single(users);
        Assert.Equal(own.Id, listed.Id);
        Assert.Equal(Roles.Accountant, listed.Role);
    }

    [Fact]
    public async Task Deactivation_changes_security_stamp_and_blocks_cross_tenant_target()
    {
        var fixture = CreateFixture();
        var own = AddUser(fixture, fixture.TenantId, Roles.Accountant, "contador@empresa.test");
        var other = AddUser(fixture, Guid.NewGuid(), Roles.Seller, "outro@empresa.test");
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var originalStamp = own.SecurityStamp;
        fixture.Db.RefreshTokens.Add(new RefreshToken
        {
            TenantId = fixture.TenantId,
            UserId = own.Id,
            TokenHash = new string('A', 64),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        });
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await fixture.Service.SetActiveAsync(fixture.TenantId, Guid.NewGuid(), own.Id, false,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsActive);
        Assert.False(own.IsActive);
        Assert.NotEqual(originalStamp, own.SecurityStamp);
        Assert.NotNull((await fixture.Db.RefreshTokens.SingleAsync(TestContext.Current.CancellationToken)).RevokedAt);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Service.SetActiveAsync(
            fixture.TenantId, Guid.NewGuid(), other.Id, false, TestContext.Current.CancellationToken));
    }

    private static ApplicationUser AddUser(Fixture fixture, Guid tenantId, string roleName, string email)
    {
        var role = fixture.Db.Roles.Local.SingleOrDefault(x => x.Name == roleName);
        if (role is null)
        {
            role = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = roleName, NormalizedName = roleName.ToUpperInvariant() };
            fixture.Db.Roles.Add(role);
        }
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = email,
            SecurityStamp = Guid.NewGuid().ToString("N"), RequiresPasswordChange = true
        };
        fixture.Db.Users.Add(user);
        fixture.Db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = role.Id });
        return user;
    }

    private static Fixture CreateFixture()
    {
        var tenantId = Guid.NewGuid();
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new StubTenantContext(tenantId));
        return new Fixture(db, new UserAccessService(db, TimeProvider.System), tenantId);
    }

    private sealed record StubTenantContext(Guid? TenantId) : ITenantContext;
    private sealed record Fixture(AppDbContext Db, UserAccessService Service, Guid TenantId);
}
