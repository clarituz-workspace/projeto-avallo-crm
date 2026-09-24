using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Infrastructure;
using Xunit;

namespace Avallo.Tests.Infrastructure;

public sealed class AppDbContextTenantTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Query_returns_only_current_tenant_data()
    {
        var options = CreateOptions();
        await SeedAsync(options);

        await using var context = new AppDbContext(options, new StubTenantContext(TenantA));

        var tokens = await context.RefreshTokens.ToListAsync(TestContext.Current.CancellationToken);

        var token = Assert.Single(tokens);
        Assert.Equal(TenantA, token.TenantId);
    }

    [Fact]
    public async Task SaveChanges_rejects_cross_tenant_modification()
    {
        var options = CreateOptions();
        await SeedAsync(options);
        await using var context = new AppDbContext(options, new StubTenantContext(TenantA));
        var otherTenantToken = await context.RefreshTokens.IgnoreQueryFilters()
            .SingleAsync(x => x.TenantId == TenantB, TestContext.Current.CancellationToken);
        otherTenantToken.RevokedAt = DateTimeOffset.UtcNow;

        var action = () => context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(action);
    }

    [Fact]
    public async Task Financial_entries_are_aggregated_only_for_current_tenant()
    {
        var options = CreateOptions();
        await using (var seed = new AppDbContext(options, new StubTenantContext(null)))
        {
            seed.FinancialEntries.AddRange(CreateEntry(TenantA, 100), CreateEntry(TenantB, 900));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = new AppDbContext(options, new StubTenantContext(TenantA));
        var billed = await context.FinancialEntries.SumAsync(
            x => x.GrossAmount, TestContext.Current.CancellationToken);

        Assert.Equal(100, billed);
    }

    private static DbContextOptions<AppDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static async Task SeedAsync(DbContextOptions<AppDbContext> options)
    {
        await using var context = new AppDbContext(options, new StubTenantContext(null));
        context.RefreshTokens.AddRange(CreateToken(TenantA), CreateToken(TenantB));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static RefreshToken CreateToken(Guid tenantId) => new()
    {
        TenantId = tenantId,
        UserId = UserId,
        TokenHash = Guid.NewGuid().ToString("N").PadRight(64, '0'),
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
    };

    private static FinancialEntry CreateEntry(Guid tenantId, decimal amount) => new()
    {
        TenantId = tenantId,
        ExternalId = Guid.NewGuid().ToString("N"),
        Description = "Test entry",
        Marketplace = "Test marketplace",
        PaymentMethod = "Test payment",
        Status = "Received",
        OccurredAt = DateTimeOffset.UtcNow,
        GrossAmount = amount,
        ReceivedAmount = amount
    };

    private sealed record StubTenantContext(Guid? TenantId) : ITenantContext;
}
