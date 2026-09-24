using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Features.Auth;
using Avallo.Web.Features.Expenses;
using Avallo.Web.Infrastructure;

namespace Avallo.Web.Features.Inventory;

public static class InventoryEndpoints
{
    private const long MaximumXmlSize = 10 * 1024 * 1024;

    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/inventory").WithTags("Inventory")
            .RequireAuthorization(Policies.TenantMember);
        group.MapGet("/overview", GetOverviewAsync);
        group.MapPost("/items", CreateItemAsync).RequireAuthorization(Policies.CanWrite);
        group.MapPut("/items/{id:guid}", UpdateItemAsync).RequireAuthorization(Policies.CanWrite);
        group.MapDelete("/items/{id:guid}", DeleteItemAsync).RequireAuthorization(Policies.CanWrite);
        group.MapPost("/items/{id:guid}/damage", RecordDamageAsync).RequireAuthorization(Policies.CanWrite);
        group.MapGet("/invoices/{id:guid}", GetInvoiceAsync);
        group.MapPut("/invoices/{id:guid}", UpdateInvoiceAsync).RequireAuthorization(Policies.CanWrite);
        group.MapDelete("/invoices/{id:guid}", VoidInvoiceAsync).RequireAuthorization(Policies.CanWrite);
        group.MapPost("/invoices/preview", PreviewInvoiceAsync)
            .RequireAuthorization(Policies.CanWrite).DisableAntiforgery();
        group.MapPost("/invoices/import", ImportInvoiceAsync)
            .RequireAuthorization(Policies.CanWrite).DisableAntiforgery();
        group.MapPost("/mappings", CreateMappingAsync).RequireAuthorization(Policies.CanWrite);
        group.MapPost("/reprocess", ReprocessAsync).RequireAuthorization(Policies.CanWrite);
        return endpoints;
    }

    private static async Task<IResult> RecordDamageAsync(
        Guid id, RecordInventoryDamageRequest request, InventoryCostService inventory,
        CancellationToken cancellationToken)
    {
        if (request.Quantity <= 0)
            return Results.ValidationProblem(new Dictionary<string, string[]>
                { ["quantity"] = ["A quantidade avariada deve ser maior que zero."] });
        if (request.OperationId == Guid.Empty)
            return Results.ValidationProblem(new Dictionary<string, string[]>
                { ["operationId"] = ["Informe um identificador da operacao."] });
        try
        {
            var result = await inventory.RecordDamageWithoutRefundAsync(
                id, request.Quantity, request.Description, request.OperationId, cancellationToken);
            return Results.Ok(result);
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
    }

    private static async Task<IResult> PreviewInvoiceAsync(
        IFormFile file, INfeXmlParser parser, AppDbContext db, CancellationToken cancellationToken)
    {
        var parsed = await ParseInvoiceAsync(file, parser, cancellationToken);
        if (parsed.Error is not null)
            return parsed.Error;

        var invoice = parsed.Invoice!;
        var alreadyImported = await db.SupplierInvoices
            .AnyAsync(x => x.AccessKey == invoice.AccessKey, cancellationToken);
        return Results.Ok(new SupplierInvoicePreviewResponse(
            invoice.AccessKey, invoice.InvoiceNumber, invoice.Series, invoice.IssuedAt,
            invoice.SupplierTaxId, invoice.SupplierName, invoice.Total, alreadyImported,
            invoice.Items.Select(x => new SupplierInvoiceItemPreviewResponse(
                x.SupplierSku, x.Barcode, x.Name, x.Quantity, x.UnitCost, x.Total)).ToArray()));
    }

    private static async Task<IResult> CreateItemAsync(
        CreateInventoryItemRequest request, AppDbContext db, ITenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        var sku = request.Sku?.Trim();
        var name = request.Name?.Trim();
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(sku)) errors["sku"] = ["SKU is required."];
        if (string.IsNullOrWhiteSpace(name)) errors["name"] = ["Product name is required."];
        if (request.QuantityOnHand < 0) errors["quantityOnHand"] = ["Quantity cannot be negative."];
        if (request.AverageUnitCost < 0) errors["averageUnitCost"] = ["Unit cost cannot be negative."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (await db.InventoryItems.AnyAsync(x => x.Sku == sku, cancellationToken))
            return Results.Conflict(new { message = "An inventory item with this SKU already exists." });

        var item = new InventoryItem
        {
            TenantId = tenantContext.TenantId!.Value,
            Sku = sku!,
            Name = name!,
            QuantityOnHand = request.QuantityOnHand,
            AverageUnitCost = request.AverageUnitCost
        };
        db.InventoryItems.Add(item);
        if (request.QuantityOnHand > 0)
        {
            db.InventoryMovements.Add(new InventoryMovement
            {
                TenantId = item.TenantId,
                InventoryItemId = item.Id,
                Type = InventoryMovementTypes.ManualOpeningBalance,
                Quantity = request.QuantityOnHand,
                UnitCost = request.AverageUnitCost,
                Total = request.QuantityOnHand * request.AverageUnitCost,
                EventKey = $"manual-opening:{item.Id:N}",
                OccurredAt = DateTimeOffset.UtcNow
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/inventory/items/{item.Id}", new InventoryItemResponse(
            item.Id, item.Sku, item.Name, item.QuantityOnHand, item.AverageUnitCost,
            item.QuantityOnHand * item.AverageUnitCost, []));
    }

    private static async Task<InventoryOverviewResponse> GetOverviewAsync(
        AppDbContext db, CancellationToken cancellationToken)
    {
        var itemData = await db.InventoryItems.AsNoTracking().Where(x => !x.IsArchived)
            .OrderBy(x => x.Sku)
            .Select(x => new { x.Id, x.Sku, x.Name, x.QuantityOnHand, x.AverageUnitCost })
            .ToArrayAsync(cancellationToken);
        var mappingData = await db.MarketplaceSkuMappings.AsNoTracking()
            .OrderBy(x => x.Platform).ThenBy(x => x.ExternalSku)
            .Select(x => new { x.Id, x.InventoryItemId, x.Platform, x.ExternalSku })
            .ToArrayAsync(cancellationToken);
        var mappingsByItem = mappingData.ToLookup(m => m.InventoryItemId);
        var items = itemData.Select(x => new InventoryItemResponse(
            x.Id, x.Sku, x.Name, x.QuantityOnHand, x.AverageUnitCost,
            x.QuantityOnHand * x.AverageUnitCost,
            mappingsByItem[x.Id]
                .Select(m => new SkuMappingResponse(m.Id, m.Platform, m.ExternalSku)).ToArray())).ToArray();
        var issuesData = await (
            from issue in db.InventoryReconciliationIssues.AsNoTracking()
            join order in db.MarketplaceOrders.AsNoTracking() on issue.MarketplaceOrderId equals order.Id
            join item in db.MarketplaceOrderItems.AsNoTracking() on issue.MarketplaceOrderItemId equals item.Id
            where issue.ResolvedAt == null
            orderby issue.CreatedAt descending
            select new
            {
                issue.Id, issue.Type, issue.Details, issue.CreatedAt,
                order.OrderId, order.Platform, ExternalSku = item.Sku, ItemName = item.Title
            }).ToArrayAsync(cancellationToken);
        var issues = issuesData.Select(x => new InventoryIssueResponse(
            x.Id, x.Type, x.Details, x.OrderId, x.Platform, x.ExternalSku,
            x.ItemName, x.CreatedAt)).ToArray();
        var invoices = await db.SupplierInvoices.AsNoTracking().Where(x => x.VoidedAt == null).OrderByDescending(x => x.IssuedAt)
            .Select(x => new SupplierInvoiceResponse(
                x.Id, x.AccessKey, x.InvoiceNumber, x.Series, x.IssuedAt,
                x.SupplierTaxId, x.SupplierName, x.Total, x.Items.Count, x.ImportedAt))
            .ToArrayAsync(cancellationToken);
        return new InventoryOverviewResponse(items, issues, invoices);
    }

    private static async Task<IResult> UpdateItemAsync(Guid id, UpdateInventoryItemRequest request,
        AppDbContext db, CancellationToken cancellationToken)
    {
        var item = await db.InventoryItems.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null) return Results.NotFound(new { message = "Produto de estoque nao localizado." });
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Informe o nome do produto."] });
        item.Name = request.Name.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new InventoryItemResponse(item.Id, item.Sku, item.Name, item.QuantityOnHand,
            item.AverageUnitCost, item.QuantityOnHand * item.AverageUnitCost, []));
    }

    private static async Task<IResult> DeleteItemAsync(Guid id, bool force, AppDbContext db,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var item = await db.InventoryItems.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null) return Results.NotFound(new { message = "Produto de estoque nao localizado." });
        var hasHistory = await db.InventoryMovements.AnyAsync(x => x.InventoryItemId == id, cancellationToken)
            || await db.SupplierInvoiceItems.AnyAsync(x => x.InventoryItemId == id, cancellationToken);
        var mappings = await db.MarketplaceSkuMappings.Where(x => x.InventoryItemId == id).ToArrayAsync(cancellationToken);
        var hasLinks = item.QuantityOnHand != 0 || hasHistory || mappings.Length > 0;
        if (hasLinks && !force)
            return Results.Conflict(new
            {
                message = "O produto possui saldo ou historico. Deseja desvincular e excluir completamente?",
                canForceDelete = true
            });
        if (force)
        {
            db.MarketplaceSkuMappings.RemoveRange(mappings);
            if (item.QuantityOnHand != 0)
            {
                db.InventoryMovements.Add(new InventoryMovement
                {
                    TenantId = item.TenantId, InventoryItemId = item.Id,
                    Type = InventoryMovementTypes.ManualWriteOff, Quantity = -item.QuantityOnHand,
                    UnitCost = item.AverageUnitCost,
                    Total = decimal.Round(item.QuantityOnHand * item.AverageUnitCost, 2),
                    EventKey = $"inventory-item:{item.Id}:archive:{timeProvider.GetUtcNow().ToUnixTimeMilliseconds()}",
                    OccurredAt = timeProvider.GetUtcNow()
                });
            }
            item.QuantityOnHand = 0;
            item.AverageUnitCost = 0;
            item.IsArchived = true;
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        }
        db.InventoryItems.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetInvoiceAsync(Guid id, AppDbContext db, CancellationToken cancellationToken)
    {
        var invoice = await db.SupplierInvoices.AsNoTracking().Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id && x.VoidedAt == null, cancellationToken);
        return invoice is null ? Results.NotFound(new { message = "Nota fiscal nao localizada." }) :
            Results.Ok(ToDetail(invoice));
    }

    private static async Task<IResult> UpdateInvoiceAsync(Guid id, UpdateSupplierInvoiceRequest request,
        AppDbContext db, CancellationToken cancellationToken)
    {
        var invoice = await db.SupplierInvoices.Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id && x.VoidedAt == null, cancellationToken);
        if (invoice is null) return Results.NotFound(new { message = "Nota fiscal nao localizada." });
        if (string.IsNullOrWhiteSpace(request.SupplierName))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["supplierName"] = ["Informe o fornecedor."] });
        invoice.SupplierName = request.SupplierName.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToDetail(invoice));
    }

    private static async Task<IResult> VoidInvoiceAsync(Guid id, AppDbContext db, IExpenseStorage storage,
        TimeProvider timeProvider, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var invoice = await db.SupplierInvoices.Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id && x.VoidedAt == null, cancellationToken);
        if (invoice is null) return Results.NotFound(new { message = "Nota fiscal nao localizada." });
        var itemIds = invoice.Items.Select(x => x.InventoryItemId).Distinct().ToArray();
        var inventoryItems = await db.InventoryItems.Where(x => itemIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        foreach (var line in invoice.Items)
            if (!inventoryItems.TryGetValue(line.InventoryItemId, out var stock) || stock.QuantityOnHand < line.Quantity)
                return Results.Conflict(new { message = $"Estoque insuficiente para estornar o item {line.SupplierSku}." });

        var now = timeProvider.GetUtcNow();
        foreach (var line in invoice.Items)
        {
            var stock = inventoryItems[line.InventoryItemId];
            var remainingQuantity = stock.QuantityOnHand - line.Quantity;
            var remainingValue = stock.QuantityOnHand * stock.AverageUnitCost - line.Quantity * line.UnitCost;
            stock.QuantityOnHand = remainingQuantity;
            stock.AverageUnitCost = remainingQuantity == 0 ? 0 : Math.Max(0, decimal.Round(remainingValue / remainingQuantity, 6));
            var original = await db.InventoryMovements.SingleAsync(
                x => x.SupplierInvoiceItemId == line.Id && x.Type == InventoryMovementTypes.PurchaseReceipt, cancellationToken);
            db.InventoryMovements.Add(new InventoryMovement
            {
                TenantId = invoice.TenantId, InventoryItemId = line.InventoryItemId,
                Type = InventoryMovementTypes.PurchaseReceiptReversal, Quantity = -line.Quantity,
                UnitCost = line.UnitCost, Total = line.Total,
                EventKey = $"nfe:{invoice.AccessKey}:item:{line.Id}:void", OccurredAt = now,
                SupplierInvoiceItemId = line.Id, ReversesMovementId = original.Id
            });
        }
        var originalEntry = await db.AccountingEntries.Include(x => x.Postings).SingleAsync(
            x => x.SourceType == "SupplierInvoice" && x.SourceId == invoice.Id.ToString()
                 && x.Type == AccountingEntryTypes.PurchaseReceipt, cancellationToken);
        var reversal = new AccountingEntry
        {
            TenantId = invoice.TenantId, EventKey = $"nfe:{invoice.AccessKey}:void",
            Type = AccountingEntryTypes.Reversal, SourceType = "SupplierInvoice",
            SourceId = invoice.Id.ToString(), Description = $"Estorno da NF-e {invoice.InvoiceNumber}",
            OccurredAt = now, ReversesEntryId = originalEntry.Id
        };
        reversal.Postings.AddRange(originalEntry.Postings.Select(x => new AccountingPosting
        {
            TenantId = invoice.TenantId, AccountingEntryId = reversal.Id, AccountCode = x.AccountCode,
            AccountName = x.AccountName, Marketplace = x.Marketplace, Currency = x.Currency,
            Debit = x.Credit, Credit = x.Debit
        }));
        db.AccountingEntries.Add(reversal);
        invoice.VoidedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(invoice.XmlObjectKey))
        {
            try { await storage.DeleteAsync(invoice.XmlObjectKey, cancellationToken); }
            catch (Exception exception)
            {
                loggerFactory.CreateLogger("Inventory").LogError(exception,
                    "NF-e {InvoiceId} foi estornada, mas o XML {ObjectKey} nao foi removido.", invoice.Id, invoice.XmlObjectKey);
            }
        }
        return Results.NoContent();
    }

    private static SupplierInvoiceDetailResponse ToDetail(SupplierInvoice invoice) => new(
        invoice.Id, invoice.AccessKey, invoice.InvoiceNumber, invoice.Series, invoice.IssuedAt,
        invoice.SupplierTaxId, invoice.SupplierName, invoice.Total, invoice.ImportedAt,
        invoice.Items.Select(x => new SupplierInvoiceItemPreviewResponse(
            x.SupplierSku, x.Barcode, x.Name, x.Quantity, x.UnitCost, x.Total)).ToArray());

    private static async Task<IResult> ImportInvoiceAsync(
        IFormFile file,
        INfeXmlParser parser,
        InventoryCostService inventory,
        IExpenseStorage storage,
        ITenantContext tenantContext,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (file.Length is <= 0 or > MaximumXmlSize)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["NF-e XML must be between 1 byte and 10 MB."] });
        await using var memory = new MemoryStream((int)file.Length);
        await file.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        ParsedNfeInvoice parsed;
        try
        {
            memory.Position = 0;
            parsed = parser.Parse(memory);
        }
        catch (InvalidDataException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [exception.Message] });
        }
        if (await db.SupplierInvoices.AnyAsync(x => x.AccessKey == parsed.AccessKey, cancellationToken))
            return Results.Conflict(new { message = "This NF-e has already been imported." });
        var tenantId = tenantContext.TenantId!.Value;
        var objectKey = $"tenants/{tenantId:N}/inventory/invoices/{parsed.AccessKey}.xml";
        memory.Position = 0;
        await storage.PutAsync(objectKey, memory, "application/xml", cancellationToken);
        try
        {
            var invoice = await inventory.ImportSupplierInvoiceAsync(
                parsed, cancellationToken, objectKey,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            await inventory.ReprocessOpenIssuesAsync(cancellationToken);
            return Results.Ok(new SupplierInvoiceResponse(
                invoice.Id, invoice.AccessKey, invoice.InvoiceNumber, invoice.Series, invoice.IssuedAt,
                invoice.SupplierTaxId, invoice.SupplierName, invoice.Total, invoice.Items.Count, invoice.ImportedAt));
        }
        catch
        {
            await storage.DeleteAsync(objectKey, cancellationToken);
            throw;
        }
    }

    private static async Task<(ParsedNfeInvoice? Invoice, IResult? Error)> ParseInvoiceAsync(
        IFormFile file, INfeXmlParser parser, CancellationToken cancellationToken)
    {
        if (file.Length is <= 0 or > MaximumXmlSize)
            return (null, Results.ValidationProblem(new Dictionary<string, string[]>
                { ["file"] = ["NF-e XML must be between 1 byte and 10 MB."] }));
        await using var memory = new MemoryStream((int)file.Length);
        await file.CopyToAsync(memory, cancellationToken);
        try
        {
            memory.Position = 0;
            return (parser.Parse(memory), null);
        }
        catch (InvalidDataException exception)
        {
            return (null, Results.ValidationProblem(new Dictionary<string, string[]>
                { ["file"] = [exception.Message] }));
        }
    }

    private static async Task<IResult> CreateMappingAsync(
        CreateSkuMappingRequest request,
        AppDbContext db,
        InventoryCostService inventory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Platform) || string.IsNullOrWhiteSpace(request.ExternalSku))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["mapping"] = ["Platform and external SKU are required."] });
        if (!await db.InventoryItems.AnyAsync(x => x.Id == request.InventoryItemId, cancellationToken))
            return Results.NotFound(new { message = "Inventory item was not found." });
        var platform = request.Platform.Trim().ToLowerInvariant();
        var externalSku = request.ExternalSku.Trim();
        var existing = await db.MarketplaceSkuMappings
            .SingleOrDefaultAsync(x => x.Platform == platform && x.ExternalSku == externalSku, cancellationToken);
        if (existing is not null)
            return existing.InventoryItemId == request.InventoryItemId
                ? Results.Ok(new SkuMappingResponse(existing.Id, existing.Platform, existing.ExternalSku))
                : Results.Conflict(new { message = "This marketplace SKU is already mapped to another inventory item." });
        var mapping = new MarketplaceSkuMapping
        {
            Platform = platform,
            ExternalSku = externalSku,
            InventoryItemId = request.InventoryItemId
        };
        db.MarketplaceSkuMappings.Add(mapping);
        await db.SaveChangesAsync(cancellationToken);
        await inventory.ReprocessOpenIssuesAsync(cancellationToken);
        return Results.Ok(new SkuMappingResponse(mapping.Id, mapping.Platform, mapping.ExternalSku));
    }

    private static async Task<ReprocessInventoryResponse> ReprocessAsync(
        InventoryCostService inventory, CancellationToken cancellationToken) =>
        new(await inventory.ReprocessOpenIssuesAsync(cancellationToken));
}

public sealed record InventoryOverviewResponse(
    InventoryItemResponse[] Items,
    InventoryIssueResponse[] Issues,
    SupplierInvoiceResponse[] Invoices);
public sealed record InventoryItemResponse(
    Guid Id, string Sku, string Name, decimal QuantityOnHand, decimal AverageUnitCost,
    decimal InventoryValue, SkuMappingResponse[] Mappings);
public sealed record SkuMappingResponse(Guid Id, string Platform, string ExternalSku);
public sealed record InventoryIssueResponse(
    Guid Id, string Type, string Details, string OrderId, string Platform,
    string? ExternalSku, string ItemName, DateTimeOffset CreatedAt);
public sealed record SupplierInvoiceResponse(
    Guid Id, string AccessKey, string InvoiceNumber, string Series, DateTimeOffset IssuedAt,
    string SupplierTaxId, string SupplierName, decimal Total, int ItemCount, DateTimeOffset ImportedAt);
public sealed record CreateSkuMappingRequest(string Platform, string ExternalSku, Guid InventoryItemId);
public sealed record CreateInventoryItemRequest(
    string Sku, string Name, decimal QuantityOnHand, decimal AverageUnitCost);
public sealed record UpdateInventoryItemRequest(string Name);
public sealed record RecordInventoryDamageRequest(decimal Quantity, string? Description, Guid OperationId);
public sealed record UpdateSupplierInvoiceRequest(string SupplierName);
public sealed record SupplierInvoiceDetailResponse(
    Guid Id, string AccessKey, string InvoiceNumber, string Series, DateTimeOffset IssuedAt,
    string SupplierTaxId, string SupplierName, decimal Total, DateTimeOffset ImportedAt,
    SupplierInvoiceItemPreviewResponse[] Items);
public sealed record SupplierInvoicePreviewResponse(
    string AccessKey, string InvoiceNumber, string Series, DateTimeOffset IssuedAt,
    string SupplierTaxId, string SupplierName, decimal Total, bool AlreadyImported,
    SupplierInvoiceItemPreviewResponse[] Items);
public sealed record SupplierInvoiceItemPreviewResponse(
    string SupplierSku, string? Barcode, string Name, decimal Quantity, decimal UnitCost, decimal Total);
public sealed record ReprocessInventoryResponse(int ProcessedItems);
