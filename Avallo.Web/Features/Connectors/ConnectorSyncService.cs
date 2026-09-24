using Avallo.Connectors.Abstractions;
using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Infrastructure;
using Avallo.Web.Features.Accounting;
using Avallo.Web.Features.Inventory;
using Avallo.Web.Features.Fiscal;

namespace Avallo.Web.Features.Connectors;

public sealed class ConnectorSyncService(
    AppDbContext db,
    ConnectorGateway gateway,
    AccountingEngine accountingEngine,
    InventoryCostService inventoryCost,
    TaxEngine taxEngine,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
{
    public async Task<SyncResult> SyncAllAsync(Guid connectionId, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var execution = await gateway.GetExecutionAsync(connectionId, cancellationToken);
        var buffer = new List<FinancialOrderData>(100);
        var processed = 0;
        await foreach (var order in execution.Connector.SyncAllAsync(execution.Context, since, cancellationToken))
        {
            ValidateOrder(order, execution.Connector.Descriptor.Name);
            var payments = await execution.Connector.GetPaymentsAsync(execution.Context, order.OrderId, cancellationToken);
            var fees = (await execution.Connector.GetFeesAsync(execution.Context, order.OrderId, cancellationToken)).ToList();
            fees.AddRange(payments.Where(x => x.PaymentFee > 0).Select(x => new StandardFee(
                "payment_fee", $"Taxa do pagamento {x.PaymentId}", x.PaymentFee, x.Currency,
                StandardFeeCategory.PaymentProcessing, $"payment:{x.PaymentId}:processing")));
            if (fees.Count == 0 && order.PlatformFee > 0)
                fees.Add(new StandardFee("platform_fee", "Taxa agregada do conector", order.PlatformFee,
                    order.Currency, StandardFeeCategory.Other, "legacy:platform_fee"));
            buffer.Add(new FinancialOrderData(order, payments, fees));
            if (buffer.Count < 100)
                continue;
            processed += await UpsertBatchAsync(execution.Connection, buffer, cancellationToken);
            buffer.Clear();
        }
        if (buffer.Count > 0)
            processed += await UpsertBatchAsync(execution.Connection, buffer, cancellationToken);
        execution.Connection.LastSyncAt = timeProvider.GetUtcNow();
        execution.Connection.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return new SyncResult(processed, execution.Connection.LastSyncAt.Value);
    }

    private async Task<int> UpsertBatchAsync(
        MarketplaceConnection connection,
        IReadOnlyCollection<FinancialOrderData> orders,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId!.Value;
        var ids = orders.Select(x => x.Order.OrderId).ToArray();
        var existingOrders = await db.MarketplaceOrders.Include(x => x.Items)
            .Where(x => x.ConnectionId == connection.Id && ids.Contains(x.OrderId))
            .ToDictionaryAsync(x => x.OrderId, cancellationToken);
        var entries = await db.FinancialEntries.Where(x => x.Marketplace == connection.ConnectorName && ids.Contains(x.ExternalId))
            .ToDictionaryAsync(x => x.ExternalId, cancellationToken);

        foreach (var data in orders)
        {
            var source = data.Order;
            var isNew = !existingOrders.TryGetValue(source.OrderId, out var order);
            if (isNew)
            {
                order = new MarketplaceOrder
                {
                    TenantId = tenantId, ConnectionId = connection.Id, OrderId = source.OrderId,
                    Platform = connection.ConnectorName, PaymentMethod = source.PaymentMethod,
                    Status = source.Status.ToString(), BuyerName = source.BuyerName
                };
                db.MarketplaceOrders.Add(order);
                existingOrders[source.OrderId] = order;
            }
            MapOrder(order!, source, tenantId, isNew);
            var payments = await UpsertPaymentsAsync(order!, data.Payments, tenantId, cancellationToken);
            var fees = await UpsertFeesAsync(order!, data.Fees, tenantId, cancellationToken);
            await accountingEngine.ApplyOrderAsync(order!, source, fees, payments, cancellationToken);

            if (!entries.TryGetValue(source.OrderId, out var entry))
            {
                entry = new FinancialEntry
                {
                    TenantId = tenantId, ExternalId = source.OrderId, Marketplace = connection.ConnectorName,
                    Description = string.Empty, PaymentMethod = source.PaymentMethod, Status = string.Empty,
                    OccurredAt = source.Date
                };
                db.FinancialEntries.Add(entry);
            }
            MapFinancialEntry(entry, source);
        }
        await db.SaveChangesAsync(cancellationToken);
        var eligibleOrderIds = orders.Select(x => x.Order)
            .Where(source =>
                source.FulfillmentStatus is StandardFulfillmentStatus.Delivered or StandardFulfillmentStatus.Returned ||
                source.Status == StandardOrderStatus.Cancelled)
            .Select(source => existingOrders[source.OrderId].Id)
            .ToArray();
        if (eligibleOrderIds.Length > 0)
        {
            await inventoryCost.ProcessOrdersAsync(eligibleOrderIds, cancellationToken);
            await taxEngine.ProcessOrdersAsync(eligibleOrderIds, cancellationToken);
        }
        return orders.Count;
    }

    private async Task<IReadOnlyCollection<MarketplacePayment>> UpsertPaymentsAsync(
        MarketplaceOrder order,
        IReadOnlyCollection<StandardPayment> sources,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var ids = sources.Select(x => x.PaymentId).ToArray();
        var existing = await db.MarketplacePayments
            .Where(x => x.MarketplaceOrderId == order.Id && ids.Contains(x.PaymentId))
            .ToDictionaryAsync(x => x.PaymentId, cancellationToken);
        var result = new List<MarketplacePayment>(sources.Count);
        foreach (var source in sources)
        {
            if (!existing.TryGetValue(source.PaymentId, out var payment))
            {
                payment = new MarketplacePayment
                {
                    TenantId = tenantId, MarketplaceOrderId = order.Id, PaymentId = source.PaymentId,
                    Method = source.Method, Status = source.Status.ToString()
                };
                db.MarketplacePayments.Add(payment);
            }
            payment.GrossValue = source.GrossValue;
            payment.NetValue = source.NetValue;
            payment.PaymentFee = source.PaymentFee;
            payment.PlatformFee = source.PlatformFee;
            payment.ShippingCost = source.ShippingCost;
            payment.Method = source.Method;
            payment.Status = source.Status.ToString();
            payment.Currency = source.Currency;
            payment.PaidAt = source.PaidAt;
            payment.ReleaseAt = source.ReleaseAt;
            payment.SyncedAt = timeProvider.GetUtcNow();
            result.Add(payment);
        }
        return result;
    }

    private async Task<IReadOnlyCollection<MarketplaceFee>> UpsertFeesAsync(
        MarketplaceOrder order,
        IReadOnlyCollection<StandardFee> sources,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var normalized = sources.Select((fee, index) => new
        {
            Fee = fee,
            Key = fee.ExternalId ?? $"{index}:{fee.Type}:{fee.Amount:0.00}"
        }).ToArray();
        var keys = normalized.Select(x => x.Key).ToArray();
        var existing = await db.MarketplaceFees
            .Where(x => x.MarketplaceOrderId == order.Id && keys.Contains(x.ExternalKey))
            .ToDictionaryAsync(x => x.ExternalKey, cancellationToken);
        var result = new List<MarketplaceFee>(normalized.Length);
        foreach (var item in normalized)
        {
            if (!existing.TryGetValue(item.Key, out var fee))
            {
                fee = new MarketplaceFee
                {
                    TenantId = tenantId, MarketplaceOrderId = order.Id, ExternalKey = item.Key,
                    Type = item.Fee.Type, Category = item.Fee.Category.ToString(), Description = item.Fee.Description
                };
                db.MarketplaceFees.Add(fee);
            }
            fee.Type = item.Fee.Type;
            fee.Category = item.Fee.Category.ToString();
            fee.Description = item.Fee.Description;
            fee.Amount = item.Fee.Amount;
            fee.Currency = item.Fee.Currency;
            fee.SyncedAt = timeProvider.GetUtcNow();
            result.Add(fee);
        }
        return result;
    }

    private void MapOrder(MarketplaceOrder target, StandardOrder source, Guid tenantId, bool isNew)
    {
        target.SaleDate = source.Date;
        target.GrossValue = source.GrossValue;
        target.PlatformFee = source.PlatformFee;
        target.NetValue = source.NetValue;
        target.PaymentMethod = source.PaymentMethod;
        target.PaymentDate = source.PaymentDate;
        target.ReleaseDate = source.ReleaseDate;
        target.Status = source.Status.ToString();
        target.FulfillmentStatus = source.FulfillmentStatus.ToString();
        target.DeliveredAt = source.DeliveredAt;
        target.Currency = source.Currency;
        target.BuyerName = source.BuyerName;
        target.InvoiceNumber = source.InvoiceNumber;
        target.SyncedAt = timeProvider.GetUtcNow();
        if (isNew)
        {
            target.Items = source.Items.Select(x => NewItem(target.Id, tenantId, x)).ToList();
            return;
        }

        var sourceItems = source.Items.ToArray();
        var sharedCount = Math.Min(target.Items.Count, sourceItems.Length);
        for (var index = 0; index < sharedCount; index++)
            MapItem(target.Items[index], sourceItems[index]);
        if (target.Items.Count > sourceItems.Length)
        {
            var removed = target.Items.Skip(sourceItems.Length).ToArray();
            db.MarketplaceOrderItems.RemoveRange(removed);
            foreach (var item in removed) target.Items.Remove(item);
        }
        for (var index = sharedCount; index < sourceItems.Length; index++)
            target.Items.Add(NewItem(target.Id, tenantId, sourceItems[index]));
    }

    private static MarketplaceOrderItem NewItem(Guid orderId, Guid tenantId, StandardOrderItem source) => new()
    {
        TenantId = tenantId, MarketplaceOrderId = orderId, Sku = source.Sku,
        Title = source.Title, Quantity = source.Quantity, UnitValue = source.UnitValue
    };

    private static void MapItem(MarketplaceOrderItem target, StandardOrderItem source)
    {
        target.Sku = source.Sku;
        target.Title = source.Title;
        target.Quantity = source.Quantity;
        target.UnitValue = source.UnitValue;
    }

    private static void MapFinancialEntry(FinancialEntry target, StandardOrder source)
    {
        target.Description = source.Items.FirstOrDefault()?.Title ?? $"Pedido {source.OrderId}";
        target.PaymentMethod = source.PaymentMethod;
        target.Status = source.Status.ToString().ToLowerInvariant();
        target.ExpectedAt = source.ReleaseDate;
        target.GrossAmount = source.GrossValue;
        target.FeeAmount = source.PlatformFee;
        target.ReceivedAmount = source.ReleaseDate <= DateTimeOffset.UtcNow ? source.NetValue : 0;
    }

    private static void ValidateOrder(StandardOrder order, string connectorName)
    {
        if (!string.Equals(order.Platform, connectorName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Connector '{connectorName}' returned order for platform '{order.Platform}'.");
        if (string.IsNullOrWhiteSpace(order.OrderId) || order.GrossValue < 0 || order.PlatformFee < 0 || order.NetValue < 0 ||
            order.Items.Any(x => x.Quantity <= 0 || x.UnitValue < 0))
            throw new InvalidOperationException($"Connector '{connectorName}' returned an invalid normalized order.");
    }

    private sealed record FinancialOrderData(
        StandardOrder Order,
        IReadOnlyCollection<StandardPayment> Payments,
        IReadOnlyCollection<StandardFee> Fees);
}

public sealed record SyncResult(int ProcessedOrders, DateTimeOffset CompletedAt);
