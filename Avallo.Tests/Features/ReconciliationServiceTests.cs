using System.Text;
using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Features.Expenses;
using Avallo.Web.Features.Reconciliation;
using Avallo.Web.Infrastructure;
using Xunit;

namespace Avallo.Tests.Features;

public sealed class ReconciliationServiceTests
{
    [Fact]
    public void Parser_reads_brazilian_csv_and_ofx()
    {
        var parser = new StatementParser();

        var csv = parser.Parse(Encoding.UTF8.GetBytes(
            "Data;Valor;Descricao;Id\n15/07/2026;1.234,56;Repasse Mercado Livre;PAY-1"), "extrato.csv");
        var ofx = parser.Parse(Encoding.UTF8.GetBytes(
            "<OFX><BANKTRANLIST><STMTTRN><DTPOSTED>20260716000000<TRNAMT>85.50<FITID>OFX-1<MEMO>REPASSE</STMTTRN></BANKTRANLIST></OFX>"), "extrato.ofx");

        Assert.Equal(1234.56m, csv.Transactions.Single().Amount);
        Assert.Equal("PAY-1", csv.Transactions.Single().ExternalId);
        Assert.Equal(new DateOnly(2026, 7, 15), csv.PeriodStart);
        Assert.Equal(85.50m, ofx.Transactions.Single().Amount);
        Assert.Equal("OFX-1", ofx.Transactions.Single().ExternalId);
    }

    [Fact]
    public async Task Reimporting_same_statement_is_rejected_without_duplicate_storage()
    {
        var fixture = CreateFixture();
        var content = Encoding.UTF8.GetBytes("Data;Valor;Descricao;Id\n15/07/2026;100,00;Repasse;BANK-1");

        await fixture.Service.ImportAsync(content, "statement.csv", "text/csv", fixture.UserId,
            TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ReconciliationConflictException>(() => fixture.Service.ImportAsync(
            content, "statement.csv", "text/csv", fixture.UserId, TestContext.Current.CancellationToken));

        Assert.Single(await fixture.Db.ReconciliationImports.ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Single(fixture.Storage.Objects);
    }

    [Fact]
    public async Task Confirmation_is_idempotent_and_posts_balanced_bank_entry()
    {
        var fixture = CreateFixture();
        var payment = await SeedPaymentAsync(fixture, 85m, "PAY-85");
        var imported = await fixture.Service.ImportAsync(
            Encoding.UTF8.GetBytes("Data;Valor;Descricao;Id\n15/07/2026;85,00;Repasse PAY-85;BANK-85"),
            "statement.csv", "text/csv", fixture.UserId, TestContext.Current.CancellationToken);
        var transaction = imported.Transactions.Single();
        var allocation = new[] { new ReconciliationAllocationRequest(payment.Id, 85m) };

        await fixture.Service.ConfirmAsync(transaction.Id, allocation, fixture.UserId,
            TestContext.Current.CancellationToken);
        await fixture.Service.ConfirmAsync(transaction.Id, allocation, fixture.UserId,
            TestContext.Current.CancellationToken);

        var entry = await fixture.Db.AccountingEntries.Include(x => x.Postings)
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AccountingEntryTypes.MarketplaceSettlement, entry.Type);
        Assert.Equal(85m, entry.Postings.Single(x => x.AccountCode == AccountingAccounts.Bank).Debit);
        Assert.Equal(85m, entry.Postings.Single(x => x.AccountCode == AccountingAccounts.MarketplaceReceivable).Credit);
        Assert.Equal(entry.Postings.Sum(x => x.Debit), entry.Postings.Sum(x => x.Credit));
        Assert.Single(await fixture.Db.ReconciliationAllocations.ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ReconciliationTransactionStatuses.Matched, transaction.Status);
    }

    [Fact]
    public async Task Closed_period_rejects_statement_and_removes_uploaded_object()
    {
        var fixture = CreateFixture();
        fixture.Db.AccountingPeriods.Add(new AccountingPeriod
        {
            TenantId = fixture.TenantId,
            Year = 2026,
            Month = 7,
            StartDate = new DateOnly(2026, 7, 1),
            EndDate = new DateOnly(2026, 7, 31),
            Status = AccountingPeriodStatuses.Closed
        });
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ImportAsync(
            Encoding.UTF8.GetBytes("Data;Valor;Descricao;Id\n15/07/2026;100,00;Repasse;BANK-CLOSED"),
            "statement.csv", "text/csv", fixture.UserId, TestContext.Current.CancellationToken));

        Assert.Empty(fixture.Storage.Objects);
        Assert.Empty(await fixture.Db.ReconciliationImports.ToArrayAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<MarketplacePayment> SeedPaymentAsync(Fixture fixture, decimal amount, string paymentId)
    {
        var order = new MarketplaceOrder
        {
            TenantId = fixture.TenantId,
            ConnectionId = Guid.NewGuid(),
            OrderId = "ORDER-85",
            Platform = "mercado-livre",
            PaymentMethod = "Pix",
            Status = "Paid",
            BuyerName = "Buyer"
        };
        var payment = new MarketplacePayment
        {
            TenantId = fixture.TenantId,
            MarketplaceOrderId = order.Id,
            PaymentId = paymentId,
            GrossValue = amount,
            NetValue = amount,
            Method = "Pix",
            Status = "Paid",
            ReleaseAt = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero)
        };
        fixture.Db.AddRange(order, payment);
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return payment;
    }

    private static Fixture CreateFixture()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new StubTenantContext(tenantId);
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
        var storage = new FakeStorage();
        var userId = Guid.NewGuid();
        var service = new ReconciliationService(db, tenant, new StatementParser(), storage,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero)));
        return new Fixture(db, service, storage, tenantId, userId);
    }

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
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            Objects[objectKey] = memory.ToArray();
        }
        public Task<byte[]?> GetAsync(string objectKey, CancellationToken cancellationToken) =>
            Task.FromResult(Objects.TryGetValue(objectKey, out var content) ? content : null);
        public string CreateDownloadUrl(string objectKey, string fileName) => objectKey;
        public string CreateReadUrl(string objectKey) => objectKey;
        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
        {
            Objects.Remove(objectKey);
            return Task.CompletedTask;
        }
    }
    private sealed record Fixture(AppDbContext Db, ReconciliationService Service, FakeStorage Storage,
        Guid TenantId, Guid UserId);
}
