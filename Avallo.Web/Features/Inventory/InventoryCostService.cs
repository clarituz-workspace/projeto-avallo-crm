using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Avallo.Web.Domain;
using Avallo.Web.Infrastructure;

namespace Avallo.Web.Features.Inventory;

public sealed record InventoryProcessingResult(int CostedItems, IReadOnlyList<InventoryReconciliationIssue> Issues);
public sealed record InventoryDamageResult(Guid MovementId, decimal Quantity, decimal Total);

public sealed class InventoryCostService(
    AppDbContext db,
    ITenantContext tenantContext,
    INfeXmlParser nfeParser,
    TimeProvider timeProvider)
{
    public async Task<InventoryDamageResult> RecordDamageWithoutRefundAsync(
        Guid inventoryItemId,
        decimal quantity,
        string? description,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Damage quantity must be greater than zero.");

        var tenantId = RequiredTenantId();
        var eventKey = $"inventory:{inventoryItemId}:damage:{operationId:N}";
        var existing = await db.InventoryMovements.SingleOrDefaultAsync(
            x => x.EventKey == eventKey, cancellationToken);
        if (existing is not null)
            return new InventoryDamageResult(existing.Id, -existing.Quantity, existing.Total);

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var item = await db.InventoryItems.SingleOrDefaultAsync(
                x => x.Id == inventoryItemId && !x.IsArchived, cancellationToken)
                ?? throw new KeyNotFoundException("Inventory item was not found.");
            if (item.QuantityOnHand < quantity)
                throw new InvalidOperationException(
                    $"Insufficient stock for damage write-off. Available: {item.QuantityOnHand:0.####}.");

            var now = timeProvider.GetUtcNow();
            var total = Money(quantity * item.AverageUnitCost);
            var movement = new InventoryMovement
            {
                TenantId = tenantId,
                InventoryItemId = item.Id,
                Type = InventoryMovementTypes.DamageWriteOff,
                Quantity = -quantity,
                UnitCost = item.AverageUnitCost,
                Total = total,
                EventKey = eventKey,
                OccurredAt = now
            };
            item.QuantityOnHand -= quantity;
            db.InventoryMovements.Add(movement);
            db.AccountingEntries.Add(CreateLedgerEntry(
                tenantId, $"{eventKey}:ledger", AccountingEntryTypes.InventoryLoss,
                "InventoryItem", item.Id.ToString(),
                string.IsNullOrWhiteSpace(description)
                    ? $"Avaria sem reembolso - {item.Sku}"
                    : description.Trim(),
                now, "operacional",
                AccountingAccounts.LossExpenses, "Despesa com perdas", total,
                AccountingAccounts.Inventory, "Estoque"));

            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return new InventoryDamageResult(movement.Id, quantity, total);
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<int> ReprocessOpenIssuesAsync(CancellationToken cancellationToken = default)
    {
        var orderIds = await db.InventoryReconciliationIssues
            .Where(x => x.ResolvedAt == null)
            .Select(x => x.MarketplaceOrderId).Distinct().ToArrayAsync(cancellationToken);
        return await ProcessOrdersAsync(orderIds, cancellationToken);
    }

    public async Task<int> ProcessOrdersAsync(
        IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0)
            return 0;

        // 1. Preload orders and their items in batch
        var ordersById = await db.MarketplaceOrders.Include(x => x.Items)
            .Where(x => orderIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        // 2. Preload MarketplaceSkuMappings for all order item SKUs in batch
        var skus = ordersById.Values.SelectMany(o => o.Items)
            .Select(i => i.Sku)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToArray();
        await db.MarketplaceSkuMappings.Include(x => x.InventoryItem)
            .Where(x => skus.Contains(x.ExternalSku))
            .ToListAsync(cancellationToken);

        // 3. Preload all existing InventoryMovements associated with these order items in batch
        var movementKeys = new List<string>();
        foreach (var order in ordersById.Values)
        {
            foreach (var item in order.Items)
            {
                movementKeys.Add($"order:{order.Platform}:{order.OrderId}:item:{item.Id}:cogs");
                movementKeys.Add($"order:{order.Platform}:{order.OrderId}:item:{item.Id}:return");
            }
        }
        var mKeysArray = movementKeys.ToArray();
        await db.InventoryMovements.Where(x => mKeysArray.Contains(x.EventKey)).ToListAsync(cancellationToken);

        // 4. Preload all existing issues associated with these movements in batch
        var issueKeys = mKeysArray.Select(k => $"{k}:issue").ToArray();
        await db.InventoryReconciliationIssues.Where(x => issueKeys.Contains(x.EventKey)).ToListAsync(cancellationToken);

        // 5. Run the batch process in a single parent transaction with a single SaveChanges
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var processed = 0;
            foreach (var orderId in orderIds)
            {
                if (!ordersById.TryGetValue(orderId, out var order))
                    throw new InvalidOperationException($"Marketplace order '{orderId}' was not found.");
                var result = await ProcessLoadedOrderAsync(order, cancellationToken);
                processed += result.CostedItems;
            }
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return processed;
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public Task<SupplierInvoice> ImportSupplierInvoiceAsync(Stream xml, CancellationToken cancellationToken = default) =>
        ImportSupplierInvoiceAsync(nfeParser.Parse(xml), cancellationToken);

    public async Task<SupplierInvoice> ImportSupplierInvoiceAsync(
        ParsedNfeInvoice parsed,
        CancellationToken cancellationToken = default,
        string? xmlObjectKey = null,
        string? xmlSha256 = null)
    {
        var tenantId = RequiredTenantId();
        var existing = await db.SupplierInvoices.Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.AccessKey == parsed.AccessKey, cancellationToken);
        if (existing is not null)
            return existing;

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var invoice = new SupplierInvoice
            {
                TenantId = tenantId,
                AccessKey = parsed.AccessKey,
                InvoiceNumber = parsed.InvoiceNumber,
                Series = parsed.Series,
                IssuedAt = parsed.IssuedAt,
                SupplierTaxId = parsed.SupplierTaxId,
                SupplierName = parsed.SupplierName,
                Total = Money(parsed.Total),
                XmlObjectKey = xmlObjectKey,
                XmlSha256 = xmlSha256,
                ImportedAt = timeProvider.GetUtcNow()
            };
            db.SupplierInvoices.Add(invoice);

            decimal receiptTotal = 0;
            for (var index = 0; index < parsed.Items.Count; index++)
            {
                var source = parsed.Items[index];
                var sku = source.SupplierSku.Trim();
                var inventoryItem = db.InventoryItems.Local.FirstOrDefault(x => x.Sku == sku) ??
                    await db.InventoryItems.SingleOrDefaultAsync(x => x.Sku == sku, cancellationToken);
                if (inventoryItem is null)
                {
                    inventoryItem = new InventoryItem
                    {
                        TenantId = tenantId,
                        Sku = sku,
                        Name = source.Name.Trim()
                    };
                    db.InventoryItems.Add(inventoryItem);
                }

                var oldValue = inventoryItem.QuantityOnHand * inventoryItem.AverageUnitCost;
                var newQuantity = inventoryItem.QuantityOnHand + source.Quantity;
                inventoryItem.QuantityOnHand = newQuantity;
                inventoryItem.AverageUnitCost = UnitCost((oldValue + source.Quantity * source.UnitCost) / newQuantity);

                var invoiceItem = new SupplierInvoiceItem
                {
                    TenantId = tenantId,
                    SupplierInvoiceId = invoice.Id,
                    InventoryItemId = inventoryItem.Id,
                    SupplierSku = sku,
                    Barcode = source.Barcode,
                    Name = source.Name.Trim(),
                    Quantity = source.Quantity,
                    UnitCost = UnitCost(source.UnitCost),
                    Total = Money(source.Total)
                };
                invoice.Items.Add(invoiceItem);
                var movementTotal = Money(source.Quantity * source.UnitCost);
                receiptTotal += movementTotal;
                db.InventoryMovements.Add(new InventoryMovement
                {
                    TenantId = tenantId,
                    InventoryItemId = inventoryItem.Id,
                    Type = InventoryMovementTypes.PurchaseReceipt,
                    Quantity = source.Quantity,
                    UnitCost = UnitCost(source.UnitCost),
                    Total = movementTotal,
                    EventKey = $"nfe:{parsed.AccessKey}:item:{index + 1}:receipt",
                    OccurredAt = parsed.IssuedAt,
                    SupplierInvoiceItemId = invoiceItem.Id
                });
            }

            var ledgerKey = $"nfe:{parsed.AccessKey}:receipt";
            db.AccountingEntries.Add(CreateLedgerEntry(
                tenantId, ledgerKey, AccountingEntryTypes.PurchaseReceipt, "SupplierInvoice", invoice.Id.ToString(),
                $"Entrada da NF-e {parsed.InvoiceNumber}", parsed.IssuedAt, "fornecedor",
                AccountingAccounts.Inventory, "Estoque", receiptTotal,
                AccountingAccounts.Suppliers, "Fornecedores a pagar"));

            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return invoice;
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<InventoryProcessingResult> ProcessOrderAsync(
        Guid marketplaceOrderId,
        CancellationToken cancellationToken = default)
    {
        var order = await db.MarketplaceOrders.Include(x => x.Items)
            .SingleAsync(x => x.Id == marketplaceOrderId, cancellationToken);
        return await ExecuteInTransactionAsync(
            () => ProcessLoadedOrderAsync(order, cancellationToken), cancellationToken);
    }

    public async Task<InventoryProcessingResult> ProcessDeliveredOrderAsync(
        Guid marketplaceOrderId,
        CancellationToken cancellationToken = default)
    {
        var order = await db.MarketplaceOrders.Include(x => x.Items)
            .SingleAsync(x => x.Id == marketplaceOrderId, cancellationToken);
        if (!string.Equals(order.FulfillmentStatus, "Delivered", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The marketplace order has not been delivered.");
        return await ExecuteInTransactionAsync(
            () => ProcessDeliveryAsync(order, cancellationToken), cancellationToken);
    }

    public async Task<InventoryProcessingResult> ProcessReturnAsync(
        Guid marketplaceOrderId,
        CancellationToken cancellationToken = default)
    {
        var order = await db.MarketplaceOrders.Include(x => x.Items)
            .SingleAsync(x => x.Id == marketplaceOrderId, cancellationToken);
        return await ExecuteInTransactionAsync(
            () => ProcessReturnAsync(order, cancellationToken), cancellationToken);
    }

    private Task<InventoryProcessingResult> ProcessLoadedOrderAsync(
        MarketplaceOrder order,
        CancellationToken cancellationToken)
    {
        if (string.Equals(order.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(order.FulfillmentStatus, "Returned", StringComparison.OrdinalIgnoreCase))
            return ProcessReturnAsync(order, cancellationToken);
        if (!string.Equals(order.FulfillmentStatus, "Delivered", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only delivered, cancelled, or returned orders can affect inventory.");
        return ProcessDeliveryAsync(order, cancellationToken);
    }

    private async Task<InventoryProcessingResult> ExecuteInTransactionAsync(
        Func<Task<InventoryProcessingResult>> action,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await action();
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<InventoryProcessingResult> ProcessDeliveryAsync(
        MarketplaceOrder order,
        CancellationToken cancellationToken)
    {
        var tenantId = RequiredTenantId();
        var issues = new List<InventoryReconciliationIssue>();
        var costed = 0;
        foreach (var orderItem in order.Items)
        {
            var movementKey = $"order:{order.Platform}:{order.OrderId}:item:{orderItem.Id}:cogs";
            if (db.InventoryMovements.Local.Any(x => x.EventKey == movementKey) ||
                await db.InventoryMovements.AnyAsync(x => x.EventKey == movementKey, cancellationToken))
                continue;

            if (string.IsNullOrWhiteSpace(orderItem.Sku))
            {
                issues.Add(await AddIssueAsync(order, orderItem, movementKey,
                    InventoryReconciliationIssueTypes.UnresolvedSku, "The order item has no SKU.", cancellationToken));
                continue;
            }

            var mapping = db.MarketplaceSkuMappings.Local.FirstOrDefault(x => x.Platform == order.Platform && x.ExternalSku == orderItem.Sku)
                ?? await db.MarketplaceSkuMappings.Include(x => x.InventoryItem)
                    .SingleOrDefaultAsync(x => x.Platform == order.Platform && x.ExternalSku == orderItem.Sku, cancellationToken);
            if (mapping is null)
            {
                issues.Add(await AddIssueAsync(order, orderItem, movementKey,
                    InventoryReconciliationIssueTypes.UnresolvedSku,
                    $"No inventory mapping exists for {order.Platform} SKU '{orderItem.Sku}'.", cancellationToken));
                continue;
            }

            var quantity = (decimal)orderItem.Quantity;
            if (mapping.InventoryItem.QuantityOnHand < quantity)
            {
                issues.Add(await AddIssueAsync(order, orderItem, movementKey,
                    InventoryReconciliationIssueTypes.InsufficientStock,
                    $"SKU '{orderItem.Sku}' requires {quantity:0.####}; available {mapping.InventoryItem.QuantityOnHand:0.####}.",
                    cancellationToken));
                continue;
            }

            var unitCost = mapping.InventoryItem.AverageUnitCost;
            var total = Money(quantity * unitCost);
            mapping.InventoryItem.QuantityOnHand -= quantity;
            db.InventoryMovements.Add(new InventoryMovement
            {
                TenantId = tenantId,
                InventoryItemId = mapping.InventoryItemId,
                Type = InventoryMovementTypes.SaleCogs,
                Quantity = -quantity,
                UnitCost = unitCost,
                Total = total,
                EventKey = movementKey,
                OccurredAt = order.DeliveredAt ?? timeProvider.GetUtcNow(),
                MarketplaceOrderItemId = orderItem.Id
            });
            db.AccountingEntries.Add(CreateLedgerEntry(
                tenantId, $"{movementKey}:ledger", AccountingEntryTypes.SaleCogs,
                "MarketplaceOrderItem", orderItem.Id.ToString(), $"CMV do pedido {order.OrderId}",
                order.DeliveredAt ?? timeProvider.GetUtcNow(), order.Platform,
                AccountingAccounts.CostOfGoodsSold, "CMV", total,
                AccountingAccounts.Inventory, "Estoque"));
            await ResolveIssueAsync(movementKey, cancellationToken);
            costed++;
        }

        return new InventoryProcessingResult(costed, issues);
    }

    private async Task<InventoryProcessingResult> ProcessReturnAsync(
        MarketplaceOrder order,
        CancellationToken cancellationToken)
    {
        var tenantId = RequiredTenantId();
        var issues = new List<InventoryReconciliationIssue>();
        var restored = 0;
        foreach (var orderItem in order.Items)
        {
            var cogsKey = $"order:{order.Platform}:{order.OrderId}:item:{orderItem.Id}:cogs";
            var returnKey = $"order:{order.Platform}:{order.OrderId}:item:{orderItem.Id}:return";
            if (db.InventoryMovements.Local.Any(x => x.EventKey == returnKey) ||
                await db.InventoryMovements.AnyAsync(x => x.EventKey == returnKey, cancellationToken))
                continue;

            var original = db.InventoryMovements.Local.FirstOrDefault(x => x.EventKey == cogsKey && x.Type == InventoryMovementTypes.SaleCogs)
                ?? await db.InventoryMovements.SingleOrDefaultAsync(x => x.EventKey == cogsKey && x.Type == InventoryMovementTypes.SaleCogs, cancellationToken);
            if (original is null)
            {
                issues.Add(await AddIssueAsync(order, orderItem, returnKey,
                    InventoryReconciliationIssueTypes.MissingOriginalCogs,
                    "The original COGS movement was not found; no stock was restored.", cancellationToken));
                continue;
            }

            var inventoryItem = db.InventoryItems.Local.FirstOrDefault(x => x.Id == original.InventoryItemId)
                ?? await db.InventoryItems.SingleAsync(x => x.Id == original.InventoryItemId, cancellationToken);
            var returnQuantity = -original.Quantity;
            var newQuantity = inventoryItem.QuantityOnHand + returnQuantity;
            inventoryItem.AverageUnitCost = newQuantity == 0 ? 0 : UnitCost(
                (inventoryItem.QuantityOnHand * inventoryItem.AverageUnitCost + returnQuantity * original.UnitCost) /
                newQuantity);
            inventoryItem.QuantityOnHand = newQuantity;
            db.InventoryMovements.Add(new InventoryMovement
            {
                TenantId = tenantId,
                InventoryItemId = original.InventoryItemId,
                Type = InventoryMovementTypes.SaleReturn,
                Quantity = returnQuantity,
                UnitCost = original.UnitCost,
                Total = original.Total,
                EventKey = returnKey,
                OccurredAt = timeProvider.GetUtcNow(),
                MarketplaceOrderItemId = orderItem.Id,
                ReversesMovementId = original.Id
            });
            db.AccountingEntries.Add(CreateLedgerEntry(
                tenantId, $"{returnKey}:ledger", AccountingEntryTypes.SaleReturn,
                "MarketplaceOrderItem", orderItem.Id.ToString(), $"Estorno de CMV do pedido {order.OrderId}",
                timeProvider.GetUtcNow(), order.Platform,
                AccountingAccounts.Inventory, "Estoque", original.Total,
                AccountingAccounts.CostOfGoodsSold, "CMV"));
            await ResolveIssueAsync(returnKey, cancellationToken);
            restored++;
        }

        return new InventoryProcessingResult(restored, issues);
    }

    private async Task<InventoryReconciliationIssue> AddIssueAsync(
        MarketplaceOrder order,
        MarketplaceOrderItem item,
        string movementKey,
        string type,
        string details,
        CancellationToken cancellationToken)
    {
        var eventKey = $"{movementKey}:issue";
        var existing = db.InventoryReconciliationIssues.Local.FirstOrDefault(x => x.EventKey == eventKey)
            ?? await db.InventoryReconciliationIssues
                .SingleOrDefaultAsync(x => x.EventKey == eventKey, cancellationToken);
        if (existing is not null)
            return existing;
        var issue = new InventoryReconciliationIssue
        {
            TenantId = RequiredTenantId(),
            EventKey = eventKey,
            Type = type,
            MarketplaceOrderId = order.Id,
            MarketplaceOrderItemId = item.Id,
            Details = details,
            CreatedAt = timeProvider.GetUtcNow()
        };
        db.InventoryReconciliationIssues.Add(issue);
        return issue;
    }

    private async Task ResolveIssueAsync(string movementKey, CancellationToken cancellationToken)
    {
        var issue = db.InventoryReconciliationIssues.Local.FirstOrDefault(x => x.EventKey == $"{movementKey}:issue" && x.ResolvedAt == null)
            ?? await db.InventoryReconciliationIssues
                .SingleOrDefaultAsync(x => x.EventKey == $"{movementKey}:issue" && x.ResolvedAt == null, cancellationToken);
        if (issue is not null)
            issue.ResolvedAt = timeProvider.GetUtcNow();
    }

    private static AccountingEntry CreateLedgerEntry(
        Guid tenantId,
        string eventKey,
        string type,
        string sourceType,
        string sourceId,
        string description,
        DateTimeOffset occurredAt,
        string marketplace,
        string debitCode,
        string debitName,
        decimal amount,
        string creditCode,
        string creditName)
    {
        var id = Guid.NewGuid();
        var entry = new AccountingEntry
        {
            Id = id,
            TenantId = tenantId,
            EventKey = eventKey,
            Type = type,
            SourceType = sourceType,
            SourceId = sourceId,
            Description = description,
            OccurredAt = occurredAt
        };
        entry.Postings.Add(new AccountingPosting
        {
            TenantId = tenantId, AccountingEntryId = id, AccountCode = debitCode, AccountName = debitName,
            Marketplace = marketplace, Debit = amount, Credit = 0
        });
        entry.Postings.Add(new AccountingPosting
        {
            TenantId = tenantId, AccountingEntryId = id, AccountCode = creditCode, AccountName = creditName,
            Marketplace = marketplace, Debit = 0, Credit = amount
        });
        return entry;
    }

    private Guid RequiredTenantId() => tenantContext.TenantId ??
        throw new UnauthorizedAccessException("A tenant is required for inventory operations.");

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
            return null;
        if (db.Database.CurrentTransaction is not null)
            return null;
        return await db.Database.BeginTransactionAsync(cancellationToken);
    }

    private static decimal UnitCost(decimal value) => decimal.Round(value, 6, MidpointRounding.AwayFromZero);
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
