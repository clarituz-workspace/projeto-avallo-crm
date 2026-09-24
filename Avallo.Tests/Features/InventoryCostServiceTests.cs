using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Features.Inventory;
using Avallo.Web.Infrastructure;
using Xunit;

namespace Avallo.Tests.Features;

public sealed class InventoryCostServiceTests
{
    [Fact]
    public async Task Damage_without_refund_writes_off_inventory_to_loss_expense_idempotently()
    {
        var (db, service, tenantId) = CreateService();
        var inventory = new InventoryItem
        {
            TenantId = tenantId, Sku = "DAMAGED-1", Name = "Damaged product",
            QuantityOnHand = 5, AverageUnitCost = 12.50m
        };
        db.InventoryItems.Add(inventory);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var operationId = Guid.NewGuid();

        var result = await service.RecordDamageWithoutRefundAsync(
            inventory.Id, 2, "Avaria no transporte sem reembolso", operationId,
            TestContext.Current.CancellationToken);
        var duplicate = await service.RecordDamageWithoutRefundAsync(
            inventory.Id, 2, "Avaria no transporte sem reembolso", operationId,
            TestContext.Current.CancellationToken);

        Assert.Equal(result, duplicate);
        Assert.Equal(3, inventory.QuantityOnHand);
        var movement = await db.InventoryMovements.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(InventoryMovementTypes.DamageWriteOff, movement.Type);
        Assert.Equal(-2, movement.Quantity);
        Assert.Equal(25, movement.Total);
        var ledger = await db.AccountingEntries.Include(x => x.Postings)
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AccountingEntryTypes.InventoryLoss, ledger.Type);
        Assert.Contains(ledger.Postings,
            x => x.AccountCode == AccountingAccounts.LossExpenses && x.Debit == 25);
        Assert.Contains(ledger.Postings,
            x => x.AccountCode == AccountingAccounts.Inventory && x.Credit == 25);
        Assert.Equal(ledger.Postings.Sum(x => x.Debit), ledger.Postings.Sum(x => x.Credit));
    }

    [Fact]
    public async Task Damage_without_refund_rejects_quantity_above_available_stock()
    {
        var (db, service, tenantId) = CreateService();
        var inventory = new InventoryItem
        {
            TenantId = tenantId, Sku = "DAMAGED-2", Name = "Product",
            QuantityOnHand = 1, AverageUnitCost = 10
        };
        db.InventoryItems.Add(inventory);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordDamageWithoutRefundAsync(
                inventory.Id, 2, null, Guid.NewGuid(), TestContext.Current.CancellationToken));

        Assert.Equal(1, inventory.QuantityOnHand);
        Assert.Empty(db.InventoryMovements);
        Assert.Empty(db.AccountingEntries);
    }

    [Fact]
    public void Parser_reads_namespace_agnostic_nfe_and_rejects_dtd()
    {
        var parser = new NfeXmlParser();
        using var xml = Xml(InvoiceXml("12345678901234567890123456789012345678901234", "SKU-1", 2, 12.345678m));

        var invoice = parser.Parse(xml);

        Assert.Equal("12345678901234567890123456789012345678901234", invoice.AccessKey);
        Assert.Equal("123", invoice.InvoiceNumber);
        Assert.Equal("Supplier Ltd", invoice.SupplierName);
        Assert.Equal(2, invoice.Items.Single().Quantity);
        Assert.Equal(12.345678m, invoice.Items.Single().UnitCost);

        using var dtd = Xml("<!DOCTYPE NFe [<!ENTITY xxe SYSTEM 'file:///etc/passwd'>]><NFe>&xxe;</NFe>");
        Assert.Throws<InvalidDataException>(() => parser.Parse(dtd));
    }

    [Fact]
    public void Parser_rejects_missing_and_duplicate_item_basics()
    {
        var parser = new NfeXmlParser();
        var missing = InvoiceXml("22345678901234567890123456789012345678901234", "SKU-1", 1, 10)
            .Replace("<xProd>Product</xProd>", string.Empty);
        using var missingXml = Xml(missing);
        Assert.Throws<InvalidDataException>(() => parser.Parse(missingXml));

        var duplicate = InvoiceXml("32345678901234567890123456789012345678901234", "SKU-1", 1, 10)
            .Replace("<cProd>SKU-1</cProd>", "<cProd>SKU-1</cProd><cProd>SKU-2</cProd>");
        using var duplicateXml = Xml(duplicate);
        Assert.Throws<InvalidDataException>(() => parser.Parse(duplicateXml));
    }

    [Fact]
    public void Parser_accepts_cpf_supplier_and_repeated_sku_lines()
    {
        var parser = new NfeXmlParser();
        var xmlText = InvoiceXml("33345678901234567890123456789012345678901234", "SKU-1", 1, 10)
            .Replace("<CNPJ>12345678000199</CNPJ>", "<CPF>12345678901</CPF>")
            .Replace("</det>", "</det><det nItem=\"2\"><prod><cProd>SKU-1</cProd><cEAN>SEM GTIN</cEAN><xProd>Product</xProd><qCom>2</qCom><vUnCom>10</vUnCom><vProd>20</vProd></prod></det>");
        using var xml = Xml(xmlText);

        var invoice = parser.Parse(xml);

        Assert.Equal("12345678901", invoice.SupplierTaxId);
        Assert.Equal(2, invoice.Items.Count);
        Assert.All(invoice.Items, item => Assert.Equal("SKU-1", item.SupplierSku));
    }

    [Fact]
    public async Task Purchases_use_weighted_average_and_post_balanced_inventory_ledger()
    {
        var (db, service, tenantId) = CreateService();
        await service.ImportSupplierInvoiceAsync(ParsedInvoice(
            "42345678901234567890123456789012345678901234", 10, 2), TestContext.Current.CancellationToken);
        await service.ImportSupplierInvoiceAsync(ParsedInvoice(
            "52345678901234567890123456789012345678901234", 5, 5), TestContext.Current.CancellationToken);

        var item = await db.InventoryItems.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(tenantId, item.TenantId);
        Assert.Equal(15, item.QuantityOnHand);
        Assert.Equal(3, item.AverageUnitCost);
        Assert.Equal(2, await db.InventoryMovements.CountAsync(TestContext.Current.CancellationToken));
        var entries = await db.AccountingEntries.Include(x => x.Postings).ToListAsync(TestContext.Current.CancellationToken);
        Assert.All(entries, entry => Assert.Equal(entry.Postings.Sum(x => x.Debit), entry.Postings.Sum(x => x.Credit)));
        Assert.All(entries, entry => Assert.Contains(entry.Postings, x => x.AccountCode == AccountingAccounts.Suppliers));
    }

    [Fact]
    public async Task Delivered_order_costs_at_current_average_without_negative_inventory()
    {
        var (db, service, tenantId) = CreateService();
        var (inventory, order) = SeedOrder(db, tenantId, quantityOnHand: 3, averageCost: 4, orderedQuantity: 2);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await service.ProcessDeliveredOrderAsync(order.Id, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.CostedItems);
        Assert.Empty(result.Issues);
        Assert.Equal(1, inventory.QuantityOnHand);
        var movement = await db.InventoryMovements.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(InventoryMovementTypes.SaleCogs, movement.Type);
        Assert.Equal(-2, movement.Quantity);
        Assert.Equal(4, movement.UnitCost);
        Assert.Equal(8, movement.Total);
        var ledger = await db.AccountingEntries.Include(x => x.Postings).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Contains(ledger.Postings, x => x.AccountCode == AccountingAccounts.CostOfGoodsSold && x.Debit == 8);
        Assert.Contains(ledger.Postings, x => x.AccountCode == AccountingAccounts.Inventory && x.Credit == 8);
    }

    [Fact]
    public async Task Return_restores_original_quantity_and_cost_and_reverses_cogs()
    {
        var (db, service, tenantId) = CreateService();
        var (inventory, order) = SeedOrder(db, tenantId, quantityOnHand: 3, averageCost: 4, orderedQuantity: 2);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await service.ProcessDeliveredOrderAsync(order.Id, TestContext.Current.CancellationToken);
        order.Status = "Cancelled";
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await service.ProcessOrderAsync(order.Id, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.CostedItems);
        Assert.Equal(3, inventory.QuantityOnHand);
        Assert.Equal(4, inventory.AverageUnitCost);
        var movements = await db.InventoryMovements.OrderBy(x => x.OccurredAt).ToListAsync(TestContext.Current.CancellationToken);
        var original = movements.Single(x => x.Type == InventoryMovementTypes.SaleCogs);
        var reversal = movements.Single(x => x.Type == InventoryMovementTypes.SaleReturn);
        Assert.Equal(original.Id, reversal.ReversesMovementId);
        Assert.Equal(-original.Quantity, reversal.Quantity);
        Assert.Equal(original.UnitCost, reversal.UnitCost);
        Assert.Equal(original.Total, reversal.Total);
        var returnLedger = await db.AccountingEntries.Include(x => x.Postings)
            .SingleAsync(x => x.Type == AccountingEntryTypes.SaleReturn, TestContext.Current.CancellationToken);
        Assert.Contains(returnLedger.Postings, x => x.AccountCode == AccountingAccounts.Inventory && x.Debit == 8);
        Assert.Contains(returnLedger.Postings, x => x.AccountCode == AccountingAccounts.CostOfGoodsSold && x.Credit == 8);
    }

    private static (AppDbContext Db, InventoryCostService Service, Guid TenantId) CreateService()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new StubTenantContext(tenantId);
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
        return (db, new InventoryCostService(db, tenant, new NfeXmlParser(), TimeProvider.System), tenantId);
    }

    private static (InventoryItem Inventory, MarketplaceOrder Order) SeedOrder(
        AppDbContext db, Guid tenantId, decimal quantityOnHand, decimal averageCost, int orderedQuantity)
    {
        var inventory = new InventoryItem
        {
            TenantId = tenantId, Sku = "INTERNAL-1", Name = "Product",
            QuantityOnHand = quantityOnHand, AverageUnitCost = averageCost
        };
        var order = new MarketplaceOrder
        {
            TenantId = tenantId, ConnectionId = Guid.NewGuid(), OrderId = "ORDER-1", Platform = "marketplace",
            PaymentMethod = "Pix", Status = "Paid", FulfillmentStatus = "Delivered", BuyerName = "Buyer",
            DeliveredAt = DateTimeOffset.UtcNow
        };
        order.Items.Add(new MarketplaceOrderItem
        {
            TenantId = tenantId, MarketplaceOrderId = order.Id, Sku = "EXTERNAL-1", Title = "Product",
            Quantity = orderedQuantity, UnitValue = 10
        });
        db.InventoryItems.Add(inventory);
        db.MarketplaceSkuMappings.Add(new MarketplaceSkuMapping
        {
            TenantId = tenantId, Platform = order.Platform, ExternalSku = "EXTERNAL-1",
            InventoryItemId = inventory.Id, InventoryItem = inventory
        });
        db.MarketplaceOrders.Add(order);
        return (inventory, order);
    }

    private static ParsedNfeInvoice ParsedInvoice(string accessKey, decimal quantity, decimal unitCost) => new(
        accessKey, "123", "1", DateTimeOffset.UtcNow, "12345678000199", "Supplier Ltd",
        decimal.Round(quantity * unitCost, 2),
        [new ParsedNfeItem("SUPPLIER-1", null, "Product", quantity, unitCost, decimal.Round(quantity * unitCost, 2))]);

    private static MemoryStream Xml(string value) => new(System.Text.Encoding.UTF8.GetBytes(value));

    private static string InvoiceXml(string accessKey, string sku, decimal quantity, decimal unitCost) => $$"""
        <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe">
          <NFe><infNFe Id="NFe{{accessKey}}">
            <ide><nNF>123</nNF><serie>1</serie><dhEmi>2026-07-19T10:00:00-03:00</dhEmi></ide>
            <emit><CNPJ>12345678000199</CNPJ><xNome>Supplier Ltd</xNome></emit>
            <det nItem="1"><prod><cProd>{{sku}}</cProd><cEAN>SEM GTIN</cEAN><xProd>Product</xProd><qCom>{{quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)}}</qCom><vUnCom>{{unitCost.ToString(System.Globalization.CultureInfo.InvariantCulture)}}</vUnCom><vProd>{{decimal.Round(quantity * unitCost, 2).ToString(System.Globalization.CultureInfo.InvariantCulture)}}</vProd></prod></det>
            <total><ICMSTot><vNF>{{decimal.Round(quantity * unitCost, 2).ToString(System.Globalization.CultureInfo.InvariantCulture)}}</vNF></ICMSTot></total>
          </infNFe></NFe>
        </nfeProc>
        """;

    private sealed record StubTenantContext(Guid? TenantId) : ITenantContext;
}
