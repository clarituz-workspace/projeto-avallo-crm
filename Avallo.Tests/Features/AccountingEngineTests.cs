using Avallo.Connectors.Abstractions;
using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Features.Accounting;
using Avallo.Web.Infrastructure;
using Xunit;

namespace Avallo.Tests.Features;

public sealed class AccountingEngineTests
{
    [Fact]
    public async Task Approved_expense_posts_once_in_the_competence_period()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new StubTenantContext(tenantId);
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
        var engine = new AccountingEngine(db, tenant, TimeProvider.System);
        var expense = new Expense
        {
            TenantId = tenantId,
            Description = "Monthly rent",
            Category = ExpenseCategories.Rent,
            CompetenceDate = new DateOnly(2026, 7, 1),
            Amount = 2500,
            CreatedByUserId = Guid.NewGuid()
        };

        await engine.ApplyExpenseApprovalAsync(expense, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await engine.ApplyExpenseApprovalAsync(expense, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var entry = await db.AccountingEntries.Include(x => x.Postings).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AccountingEntryTypes.ExpenseApproval, entry.Type);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), entry.OccurredAt);
        Assert.Equal(2500, entry.Postings.Single(x => x.AccountCode == AccountingAccounts.RentExpenses).Debit);
        Assert.Equal(2500, entry.Postings.Single(x => x.AccountCode == AccountingAccounts.OperatingPayable).Credit);
        Assert.Equal(entry.Postings.Sum(x => x.Debit), entry.Postings.Sum(x => x.Credit));
    }

    [Fact]
    public async Task Delivery_is_balanced_and_cancellation_creates_current_period_reversal()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new StubTenantContext(tenantId);
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
        var now = DateTimeOffset.UtcNow;
        var order = new MarketplaceOrder
        {
            TenantId = tenantId,
            ConnectionId = Guid.NewGuid(),
            OrderId = "ORDER-ACCOUNTING",
            Platform = "test-marketplace",
            PaymentMethod = "Pix",
            Status = "Paid",
            BuyerName = "Buyer"
        };
        var fees = new MarketplaceFee[]
        {
            new() { TenantId = tenantId, MarketplaceOrderId = order.Id, ExternalKey = "commission", Type = "commission", Category = nameof(StandardFeeCategory.MarketplaceCommission), Description = "Commission", Amount = 10 },
            new() { TenantId = tenantId, MarketplaceOrderId = order.Id, ExternalKey = "shipping", Type = "shipping", Category = nameof(StandardFeeCategory.SellerShipping), Description = "Shipping", Amount = 5 }
        };
        var engine = new AccountingEngine(db, tenant, TimeProvider.System);
        var delivered = Source(StandardOrderStatus.Paid, StandardFulfillmentStatus.Delivered, now);

        await engine.ApplyOrderAsync(order, delivered, fees, [], TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var recognition = await db.AccountingEntries.Include(x => x.Postings).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(recognition.Postings.Sum(x => x.Debit), recognition.Postings.Sum(x => x.Credit));
        Assert.Equal(115, recognition.Postings.Sum(x => x.Debit));

        var cancelled = Source(StandardOrderStatus.Cancelled, StandardFulfillmentStatus.Delivered, now);
        await engine.ApplyOrderAsync(order, cancelled, fees, [], TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, await db.AccountingEntries.CountAsync(TestContext.Current.CancellationToken));
        var reversal = await db.AccountingEntries.Include(x => x.Postings)
            .SingleAsync(x => x.Type == AccountingEntryTypes.Reversal, TestContext.Current.CancellationToken);
        Assert.Equal(recognition.Id, reversal.ReversesEntryId);
        Assert.Equal(reversal.Postings.Sum(x => x.Debit), reversal.Postings.Sum(x => x.Credit));
        Assert.Contains(reversal.Postings, x => x.AccountCode == AccountingAccounts.SalesReturns && x.Debit == 100);
    }

    private static StandardOrder Source(
        StandardOrderStatus status,
        StandardFulfillmentStatus fulfillment,
        DateTimeOffset occurredAt) => new(
        "ORDER-ACCOUNTING", "test-marketplace", occurredAt, 100, 15, 85, "Pix", occurredAt, occurredAt,
        status, "Buyer", [new StandardOrderItem("SKU", "Product", 1, 100)], null, fulfillment, occurredAt);

    private sealed record StubTenantContext(Guid? TenantId) : ITenantContext;
}
