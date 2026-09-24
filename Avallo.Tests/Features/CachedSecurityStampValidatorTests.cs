using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Avallo.Web.Domain;
using Avallo.Web.Features.Auth;
using Avallo.Web.Infrastructure;
using Xunit;

namespace Avallo.Tests.Features;

public sealed class CachedSecurityStampValidatorTests
{
    [Fact]
    public async Task Accepts_active_user_with_matching_stamp_and_serves_second_call_from_cache()
    {
        var fixture = CreateFixture();
        var user = fixture.AddUser("user@empresa.test");
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.True(await fixture.Validator.IsValidAsync(
            user.Id.ToString(), user.SecurityStamp, TestContext.Current.CancellationToken));
        Assert.Equal(1, fixture.Store.FindByIdCalls);

        // Segunda validacao sai do cache: nenhuma nova query ao banco.
        Assert.True(await fixture.Validator.IsValidAsync(
            user.Id.ToString(), user.SecurityStamp, TestContext.Current.CancellationToken));
        Assert.Equal(1, fixture.Store.FindByIdCalls);
    }

    [Fact]
    public async Task Rejects_divergent_stamp()
    {
        var fixture = CreateFixture();
        var user = fixture.AddUser("user@empresa.test");
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.False(await fixture.Validator.IsValidAsync(
            user.Id.ToString(), "outro-stamp", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rejects_inactive_user()
    {
        var fixture = CreateFixture();
        var user = fixture.AddUser("user@empresa.test", isActive: false);
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.False(await fixture.Validator.IsValidAsync(
            user.Id.ToString(), user.SecurityStamp, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rejects_missing_or_null_user_id()
    {
        var fixture = CreateFixture();

        // Usuario inexistente: uma consulta ao banco confirma e nao grava tombstone.
        Assert.False(await fixture.Validator.IsValidAsync(
            Guid.NewGuid().ToString(), "stamp", TestContext.Current.CancellationToken));
        Assert.Equal(1, fixture.Store.FindByIdCalls);

        // userId nulo nem chega ao banco.
        Assert.False(await fixture.Validator.IsValidAsync(
            null, "stamp", TestContext.Current.CancellationToken));
        Assert.Equal(1, fixture.Store.FindByIdCalls);
    }

    [Fact]
    public async Task Stale_rejecting_cache_entry_triggers_one_fresh_db_check()
    {
        var fixture = CreateFixture();
        var user = fixture.AddUser("user@empresa.test");
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Entrada defasada que rejeitaria o token (stamp antigo ainda no cache).
        await fixture.Cache.SetStringAsync(
            $"auth:secstamp:{user.Id}",
            """{"isActive":true,"securityStamp":"stamp-antigo"}""",
            TestContext.Current.CancellationToken);

        // Em vez de falhar no stale, faz uma verificacao fresca e aceita.
        Assert.True(await fixture.Validator.IsValidAsync(
            user.Id.ToString(), user.SecurityStamp, TestContext.Current.CancellationToken));
        Assert.Equal(1, fixture.Store.FindByIdCalls);

        // E a entrada foi corrigida: a proxima request sai direto do cache.
        Assert.True(await fixture.Validator.IsValidAsync(
            user.Id.ToString(), user.SecurityStamp, TestContext.Current.CancellationToken));
        Assert.Equal(1, fixture.Store.FindByIdCalls);
    }

    [Fact]
    public async Task Cache_failure_falls_back_to_database()
    {
        var tenantId = Guid.NewGuid();
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new StubTenantContext(tenantId));
        var store = new CountingUserStore(db);
        var validator = new CachedSecurityStampValidator(
            new FailingCache(),
            CreateUserManager(store),
            NullLogger<CachedSecurityStampValidator>.Instance);
        var user = AddUser(db, tenantId, "user@empresa.test");
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.True(await validator.IsValidAsync(
            user.Id.ToString(), user.SecurityStamp, TestContext.Current.CancellationToken));
        Assert.Equal(1, store.FindByIdCalls);
    }

    private static ApplicationUser AddUser(AppDbContext db, Guid tenantId, string email, bool isActive = true)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = email,
            SecurityStamp = Guid.NewGuid().ToString("N"), IsActive = isActive
        };
        db.Users.Add(user);
        return user;
    }

    private static UserManager<ApplicationUser> CreateUserManager(CountingUserStore store) =>
        new(store,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);

    private static Fixture CreateFixture()
    {
        var tenantId = Guid.NewGuid();
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new StubTenantContext(tenantId));
        var store = new CountingUserStore(db);
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var validator = new CachedSecurityStampValidator(
            cache, CreateUserManager(store), NullLogger<CachedSecurityStampValidator>.Instance);
        return new Fixture(db, validator, cache, store, tenantId);
    }

    private sealed record StubTenantContext(Guid? TenantId) : ITenantContext;

    private sealed record Fixture(
        AppDbContext Db,
        CachedSecurityStampValidator Validator,
        MemoryDistributedCache Cache,
        CountingUserStore Store,
        Guid TenantId)
    {
        public ApplicationUser AddUser(string email, bool isActive = true) =>
            CachedSecurityStampValidatorTests.AddUser(Db, TenantId, email, isActive);
    }

    private sealed class CountingUserStore(AppDbContext db)
        : UserStore<ApplicationUser, IdentityRole<Guid>, AppDbContext, Guid>(db)
    {
        public int FindByIdCalls;

        public override Task<ApplicationUser?> FindByIdAsync(
            string userId, CancellationToken cancellationToken = default)
        {
            FindByIdCalls++;
            return base.FindByIdAsync(userId, cancellationToken);
        }
    }

    private sealed class FailingCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("cache down");
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("cache down");
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            throw new InvalidOperationException("cache down");
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            throw new InvalidOperationException("cache down");
        public void Refresh(string key) => throw new InvalidOperationException("cache down");
        public Task RefreshAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("cache down");
        public void Remove(string key) => throw new InvalidOperationException("cache down");
        public Task RemoveAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("cache down");
    }
}
