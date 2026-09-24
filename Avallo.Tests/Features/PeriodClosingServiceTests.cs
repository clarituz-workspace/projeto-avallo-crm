using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Features.Expenses;
using Avallo.Web.Features.PeriodClosing;
using Avallo.Web.Infrastructure;
using QuestPDF.Infrastructure;
using Xunit;

namespace Avallo.Tests.Features;

public sealed class PeriodClosingServiceTests
{
    static PeriodClosingServiceTests() => QuestPDF.Settings.License = LicenseType.Community;

    [Fact]
    public async Task Validation_persists_blocker_checklist()
    {
        var fixture = CreateFixture();
        var order = DeliveredOrder(fixture.TenantId, new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero));
        fixture.Db.MarketplaceOrders.Add(order);
        fixture.Db.InventoryReconciliationIssues.Add(new InventoryReconciliationIssue
        {
            TenantId = fixture.TenantId,
            EventKey = "inventory:1",
            Type = InventoryReconciliationIssueTypes.InsufficientStock,
            MarketplaceOrderId = order.Id,
            MarketplaceOrderItemId = order.Items.Single().Id,
            Details = "Insufficient stock"
        });
        fixture.Db.Expenses.Add(new Expense
        {
            TenantId = fixture.TenantId,
            Description = "Rent",
            Category = ExpenseCategories.Rent,
            CompetenceDate = new DateOnly(2026, 7, 1),
            Amount = 100,
            CreatedByUserId = Guid.NewGuid()
        });
        fixture.Db.ReconciliationTransactions.Add(new ReconciliationTransaction
        {
            TenantId = fixture.TenantId,
            ReconciliationImportId = Guid.NewGuid(),
            ExternalId = "BANK-PENDING",
            OccurredAt = new DateTimeOffset(2026, 7, 12, 0, 0, 0, TimeSpan.Zero),
            Amount = 85,
            Description = "Marketplace payout"
        });
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await fixture.Service.ValidateAsync(2026, 7, Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.False(result.Passed);
        Assert.Equal(AccountingPeriodStatuses.Validating, result.Period.Status);
        Assert.Equal(7, result.Checks.Count);
        Assert.Contains(result.Checks, x => x.Code == PeriodCheckCodes.StockAndSku && x.BlockerCount == 1);
        Assert.Contains(result.Checks, x => x.Code == PeriodCheckCodes.MissingSaleCogs && !x.Passed);
        Assert.Contains(result.Checks, x => x.Code == PeriodCheckCodes.Expenses && !x.Passed);
        Assert.Contains(result.Checks, x => x.Code == PeriodCheckCodes.Taxes && !x.Passed);
        Assert.Contains(result.Checks, x => x.Code == PeriodCheckCodes.FinancialReconciliation && !x.Passed);
        Assert.Equal(7, await fixture.Db.AccountingPeriodChecks.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Balanced_empty_period_reaches_pending_accountant()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.ValidateAsync(2026, 7, Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.True(result.Passed);
        Assert.All(result.Checks, x => Assert.True(x.Passed));
        Assert.Equal(AccountingPeriodStatuses.PendingAccountant, result.Period.Status);
    }

    [Fact]
    public async Task Close_creates_deterministic_snapshot_and_transitions()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero);
        var first = CreateFixture(tenantId, now);
        var second = CreateFixture(tenantId, now);
        await SeedLedgerAndApprove(first, actorId);
        await SeedLedgerAndApprove(second, actorId);

        var firstSnapshot = await first.Service.CloseAsync(first.PeriodId, actorId, TestContext.Current.CancellationToken);
        var secondSnapshot = await second.Service.CloseAsync(second.PeriodId, actorId, TestContext.Current.CancellationToken);

        Assert.Equal(firstSnapshot.CanonicalJson, secondSnapshot.CanonicalJson);
        Assert.Equal(firstSnapshot.CanonicalJsonSha256, secondSnapshot.CanonicalJsonSha256);
        Assert.Equal(100, firstSnapshot.GrossRevenue);
        Assert.Equal(30, firstSnapshot.Cmv);
        Assert.Equal(70, firstSnapshot.Result);
        Assert.Equal($"tenants/{tenantId:N}/accounting/periods/2026-07/1.pdf", firstSnapshot.PdfObjectKey);
        Assert.NotEmpty(first.Storage.Objects[firstSnapshot.PdfObjectKey]);
        Assert.Equal(AccountingPeriodStatuses.Closed,
            (await first.Db.AccountingPeriods.SingleAsync(TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task SaveChanges_blocks_retroactive_entry_but_allows_current_reversal()
    {
        var fixture = CreateFixture();
        var period = await fixture.Service.GetOrCreateAsync(2026, 7, TestContext.Current.CancellationToken);
        period.Status = AccountingPeriodStatuses.Closed;
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        fixture.Db.AccountingEntries.Add(Entry(fixture.TenantId, "retroactive", new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero)));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken));

        fixture.Db.ChangeTracker.Clear();
        fixture.Db.AccountingEntries.Add(Entry(fixture.TenantId, "reversal", new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), AccountingEntryTypes.Reversal));
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Reopening_closed_period_preserves_snapshot()
    {
        var fixture = CreateFixture();
        var actor = Guid.NewGuid();
        await SeedLedgerAndApprove(fixture, actor);
        var snapshot = await fixture.Service.CloseAsync(fixture.PeriodId, actor, TestContext.Current.CancellationToken);

        var period = await fixture.Service.ReopenAsync(fixture.PeriodId, actor, "Late supplier document", TestContext.Current.CancellationToken);

        Assert.Equal(AccountingPeriodStatuses.Open, period.Status);
        Assert.Equal(2, period.Version);
        Assert.Equal("Late supplier document", period.ReopenReason);
        Assert.Equal(snapshot.CanonicalJsonSha256,
            (await fixture.Db.DreSnapshots.SingleAsync(TestContext.Current.CancellationToken)).CanonicalJsonSha256);
    }

    private static async Task SeedLedgerAndApprove(Fixture fixture, Guid actor)
    {
        var period = await fixture.Service.GetOrCreateAsync(2026, 7, TestContext.Current.CancellationToken);
        fixture.PeriodId = period.Id;
        fixture.Db.AccountingEntries.Add(new AccountingEntry
        {
            TenantId = fixture.TenantId,
            EventKey = "sale",
            Type = AccountingEntryTypes.DeliveryRecognition,
            SourceType = "Order",
            SourceId = "1",
            Description = "Sale",
            OccurredAt = new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero),
            Postings =
            [
                Posting(fixture.TenantId, AccountingAccounts.MarketplaceReceivable, "Receivable", 100, 0),
                Posting(fixture.TenantId, AccountingAccounts.GrossRevenue, "Revenue", 0, 100),
                Posting(fixture.TenantId, AccountingAccounts.CostOfGoodsSold, "CMV", 30, 0),
                Posting(fixture.TenantId, AccountingAccounts.Inventory, "Inventory", 0, 30)
            ]
        });
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await fixture.Service.ValidateAsync(period.Id, actor, TestContext.Current.CancellationToken);
        await fixture.Service.ApproveAsync(period.Id, actor, TestContext.Current.CancellationToken);
    }

    private static Fixture CreateFixture(Guid? tenantId = null, DateTimeOffset? now = null)
    {
        var id = tenantId ?? Guid.NewGuid();
        var tenant = new StubTenantContext(id);
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
        var storage = new FakeStorage();
        var service = new PeriodClosingService(db, tenant, storage,
            new FixedTimeProvider(now ?? new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
            new Avallo.Web.Features.Reports.ReportExportEngine());
        return new Fixture(db, service, storage, id);
    }

    private static MarketplaceOrder DeliveredOrder(Guid tenantId, DateTimeOffset deliveredAt)
    {
        var order = new MarketplaceOrder
        {
            TenantId = tenantId,
            ConnectionId = Guid.NewGuid(),
            OrderId = "ORDER-1",
            Platform = "marketplace",
            PaymentMethod = "Pix",
            Status = "Paid",
            FulfillmentStatus = "Delivered",
            DeliveredAt = deliveredAt,
            BuyerName = "Buyer"
        };
        order.Items.Add(new MarketplaceOrderItem
        {
            TenantId = tenantId,
            MarketplaceOrderId = order.Id,
            Sku = "SKU-1",
            Title = "Product",
            Quantity = 1,
            UnitValue = 10
        });
        return order;
    }

    private static AccountingEntry Entry(Guid tenantId, string key, DateTimeOffset occurredAt,
        string type = AccountingEntryTypes.DeliveryRecognition) => new()
    {
        TenantId = tenantId,
        EventKey = key,
        Type = type,
        SourceType = "Test",
        SourceId = key,
        Description = key,
        OccurredAt = occurredAt
    };

    private static AccountingPosting Posting(Guid tenantId, string code, string name, decimal debit, decimal credit) => new()
    {
        TenantId = tenantId,
        AccountCode = code,
        AccountName = name,
        Marketplace = "marketplace",
        Debit = debit,
        Credit = credit
    };

    private sealed record StubTenantContext(Guid? TenantId) : ITenantContext;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeStorage : IExpenseStorage
    {
        public Dictionary<string, byte[]> Objects { get; } = [];
        public bool IsEnabled => true;

        public async Task PutAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken)
        {
            using var stream = new MemoryStream();
            await content.CopyToAsync(stream, cancellationToken);
            Objects[objectKey] = stream.ToArray();
        }

        public Task<byte[]?> GetAsync(string objectKey, CancellationToken cancellationToken) =>
            Task.FromResult(Objects.TryGetValue(objectKey, out var content) ? content : null);

        public string CreateDownloadUrl(string objectKey, string fileName) => $"https://storage/{objectKey}?file={fileName}";
        public string CreateReadUrl(string objectKey) => $"https://storage/{objectKey}";

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
        {
            Objects.Remove(objectKey);
            return Task.CompletedTask;
        }
    }

    private sealed class Fixture(AppDbContext db, PeriodClosingService service, FakeStorage storage, Guid tenantId)
    {
        public AppDbContext Db { get; } = db;
        public PeriodClosingService Service { get; } = service;
        public FakeStorage Storage { get; } = storage;
        public Guid TenantId { get; } = tenantId;
        public Guid PeriodId { get; set; }
    }
}
