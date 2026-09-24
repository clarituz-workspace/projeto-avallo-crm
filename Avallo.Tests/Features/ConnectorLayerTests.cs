using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Avallo.Connectors.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Features.Connectors;
using Avallo.Web.Features.Accounting;
using Avallo.Web.Features.Inventory;
using Avallo.Web.Features.Fiscal;
using Avallo.Web.Infrastructure;
using Xunit;

namespace Avallo.Tests.Features;

public sealed class ConnectorLayerTests
{
    [Fact]
    public void Webhook_signature_validates_payload_and_rejects_replay_or_tampering()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var timestamp = now.ToUnixTimeSeconds().ToString();
        var body = System.Text.Encoding.UTF8.GetBytes("{\"status\":\"delivered\"}");
        var signed = System.Text.Encoding.UTF8.GetBytes(timestamp + ".").Concat(body).ToArray();
        var signature = "sha256=" + Convert.ToHexString(
            System.Security.Cryptography.HMACSHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("webhook-secret"), signed)).ToLowerInvariant();

        Assert.True(MarketplaceWebhookSignature.IsValid(
            "webhook-secret", timestamp, signature, body, now, TimeSpan.FromMinutes(10)));
        Assert.False(MarketplaceWebhookSignature.IsValid(
            "webhook-secret", timestamp, signature, System.Text.Encoding.UTF8.GetBytes("{}"), now,
            TimeSpan.FromMinutes(10)));
        Assert.False(MarketplaceWebhookSignature.IsValid(
            "webhook-secret", timestamp, signature, body, now.AddMinutes(11), TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public async Task Authentication_encrypts_tokens_at_rest()
    {
        var setup = await CreateSetupAsync();

        var connection = await setup.Gateway.AuthenticateAsync(
            "test-marketplace", new Dictionary<string, string> { ["code"] = "authorization-code" }, null,
            TestContext.Current.CancellationToken);

        Assert.NotEqual("access-token", connection.EncryptedAccessToken);
        Assert.NotEqual("refresh-token", connection.EncryptedRefreshToken);
        Assert.Equal(MarketplaceConnectionStates.Active, connection.Status);
    }

    [Fact]
    public async Task SyncAll_is_idempotent_and_populates_the_normalized_financial_model()
    {
        var setup = await CreateSetupAsync();
        var connection = await setup.Gateway.AuthenticateAsync(
            "test-marketplace", new Dictionary<string, string> { ["code"] = "authorization-code" }, null,
            TestContext.Current.CancellationToken);
        var accounting = new AccountingEngine(setup.Db, setup.TenantContext, TimeProvider.System);
        var inventory = new InventoryCostService(setup.Db, setup.TenantContext, new NfeXmlParser(), TimeProvider.System);
        var tax = new TaxEngine(setup.Db, setup.TenantContext, TimeProvider.System);
        var sync = new ConnectorSyncService(setup.Db, setup.Gateway, accounting, inventory, tax, setup.TenantContext, TimeProvider.System);

        await sync.SyncAllAsync(connection.Id, DateTimeOffset.UtcNow.AddDays(-30), TestContext.Current.CancellationToken);
        await sync.SyncAllAsync(connection.Id, DateTimeOffset.UtcNow.AddDays(-30), TestContext.Current.CancellationToken);

        Assert.Equal(1, await setup.Db.MarketplaceOrders.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await setup.Db.FinancialEntries.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await setup.Db.MarketplaceOrderItems.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await setup.Db.MarketplacePayments.CountAsync(TestContext.Current.CancellationToken));
        // 2 taxas declaradas pelo conector + 1 sintetizada da coluna PaymentFee do pagamento,
        // para que todo vazamento vire uma linha auditavel em MarketplaceFees.
        Assert.Equal(3, await setup.Db.MarketplaceFees.CountAsync(TestContext.Current.CancellationToken));
        var paymentFee = await setup.Db.MarketplaceFees.SingleAsync(
            x => x.ExternalKey == "payment:PAY-1:processing", TestContext.Current.CancellationToken);
        Assert.Equal("payment_fee", paymentFee.Type);
        Assert.Equal(nameof(StandardFeeCategory.PaymentProcessing), paymentFee.Category);
        Assert.Equal(1, paymentFee.Amount);
        var entry = await setup.Db.FinancialEntries.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(100, entry.GrossAmount);
        Assert.Equal(10, entry.FeeAmount);
        Assert.Equal(90, entry.ReceivedAmount);
        Assert.Equal(1, await setup.Db.AccountingEntries.CountAsync(TestContext.Current.CancellationToken));
        // Receita + recebivel (2) e um par debito/credito por taxa (3 fees x 2) = 8.
        Assert.Equal(8, await setup.Db.AccountingPostings.CountAsync(TestContext.Current.CancellationToken));
        var postings = await setup.Db.AccountingPostings.ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains(postings, x => x.AccountCode == AccountingAccounts.PaymentFees && x.Debit == 1);
        Assert.Contains(postings, x => x.AccountCode == AccountingAccounts.MarketplaceReceivable && x.Credit == 1);

        // O split declarado pela plataforma e persistido por pagamento e fecha:
        // bruto - comissao - taxa de pagamento - frete = liquido.
        var payment = await setup.Db.MarketplacePayments.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(100, payment.GrossValue);
        Assert.Equal(7, payment.PlatformFee);
        Assert.Equal(1, payment.PaymentFee);
        Assert.Equal(2, payment.ShippingCost);
        Assert.Equal(90, payment.NetValue);
        Assert.Equal(0,
            payment.GrossValue - payment.PlatformFee - payment.PaymentFee - payment.ShippingCost - payment.NetValue);
    }

    [Fact]
    public void Payment_split_exposes_every_field_required_by_the_architecture_document()
    {
        // Secao 03: getPayments deve devolver gross_value, platform_fee, payment_fee,
        // shipping_cost e net_value, cada um em campo proprio.
        var contract = typeof(StandardPayment).GetProperties()
            .Select(x => x.GetCustomAttributes(typeof(JsonPropertyNameAttribute), false)
                .Cast<JsonPropertyNameAttribute>().FirstOrDefault()?.Name)
            .Where(x => x is not null)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var field in new[] { "gross_value", "platform_fee", "payment_fee", "shipping_cost", "net_value" })
            Assert.Contains(field, contract);
    }

    [Fact]
    public void Payment_split_residual_is_zero_when_the_platform_reports_every_leak()
    {
        var payment = new StandardPayment(
            "PAY-1", "ORDER-1", 100, 90, "Pix", StandardPaymentStatus.Paid,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            PaymentFee: 1, Currency: "BRL", PlatformFee: 7, ShippingCost: 2);

        Assert.Equal(0, payment.SplitResidual);
    }

    private static async Task<Setup> CreateSetupAsync()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new StubTenantContext(tenantId);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new AppDbContext(options, tenantContext);
        db.Tenants.Add(new Tenant { Id = tenantId, Name = "Test tenant" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var registry = new ConnectorRegistry([new TestConnector()]);
        var gateway = new ConnectorGateway(
            db, registry, tenantContext, new EphemeralDataProtectionProvider(), TimeProvider.System);
        return new Setup(db, gateway, tenantContext);
    }

    private sealed record Setup(AppDbContext Db, ConnectorGateway Gateway, StubTenantContext TenantContext);
    private sealed record StubTenantContext(Guid? TenantId) : ITenantContext;

    private sealed class TestConnector : IMarketplaceConnector
    {
        public ConnectorDescriptor Descriptor => new("test-marketplace", "Test Marketplace", "1.0.0");

        public Task<ConnectorAuthentication> AuthenticateAsync(AuthenticationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectorAuthentication("access-token", "refresh-token", DateTimeOffset.UtcNow.AddHours(1), "account-1", "Test account"));

        public Task<ConnectorAuthentication> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectorAuthentication("renewed-token", "renewed-refresh", DateTimeOffset.UtcNow.AddHours(1), "account-1"));

        public Task<ConnectorPage<StandardOrder>> GetOrdersAsync(ConnectorContext context, OrderFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectorPage<StandardOrder>([Order()], null, false));

        public Task<StandardOrder> GetOrderDetailAsync(ConnectorContext context, string orderId, CancellationToken cancellationToken = default) => Task.FromResult(Order());
        public Task<IReadOnlyCollection<StandardPayment>> GetPaymentsAsync(ConnectorContext context, string orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<StandardPayment>>([new StandardPayment(
                "PAY-1", orderId, 100, 90, "Pix", StandardPaymentStatus.Paid,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                PaymentFee: 1, Currency: "BRL", PlatformFee: 7, ShippingCost: 2)]);
        public Task<IReadOnlyCollection<StandardFee>> GetFeesAsync(ConnectorContext context, string orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<StandardFee>>([
                new StandardFee("commission", "Commission", 7, Category: StandardFeeCategory.MarketplaceCommission, ExternalId: "commission"),
                new StandardFee("shipping", "Shipping", 3, Category: StandardFeeCategory.SellerShipping, ExternalId: "shipping")
            ]);

        public async IAsyncEnumerable<StandardOrder> SyncAllAsync(
            ConnectorContext context,
            DateTimeOffset since,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return Order();
        }

        public Task<ConnectorStatus> GetStatusAsync(ConnectorContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectorStatus(ConnectorConnectionState.Active));

        private static StandardOrder Order() => new(
            "ORDER-1", "test-marketplace", DateTimeOffset.UtcNow.AddDays(-1),
            100, 10, 90, "Pix", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            StandardOrderStatus.Paid, "Buyer", [new StandardOrderItem("SKU-1", "Product", 1, 100)], "NF-1",
            StandardFulfillmentStatus.Delivered, DateTimeOffset.UtcNow);
    }
}
