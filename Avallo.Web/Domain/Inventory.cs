namespace Avallo.Web.Domain;

public static class InventoryMovementTypes
{
    public const string PurchaseReceipt = "PurchaseReceipt";
    public const string PurchaseReceiptReversal = "PurchaseReceiptReversal";
    public const string ManualOpeningBalance = "ManualOpeningBalance";
    public const string ManualWriteOff = "ManualWriteOff";
    public const string DamageWriteOff = "DamageWriteOff";
    public const string SaleCogs = "SaleCogs";
    public const string SaleReturn = "SaleReturn";
}

public static class InventoryReconciliationIssueTypes
{
    public const string UnresolvedSku = "UnresolvedSku";
    public const string InsufficientStock = "InsufficientStock";
    public const string MissingOriginalCogs = "MissingOriginalCogs";
}

public sealed class InventoryItem : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Sku { get; init; }
    public required string Name { get; set; }
    public decimal QuantityOnHand { get; set; }
    public decimal AverageUnitCost { get; set; }
    public bool IsArchived { get; set; }
}

public sealed class MarketplaceSkuMapping : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Platform { get; init; }
    public required string ExternalSku { get; init; }
    public Guid InventoryItemId { get; init; }
    public InventoryItem InventoryItem { get; init; } = null!;
}

public sealed class SupplierInvoice : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string AccessKey { get; init; }
    public required string InvoiceNumber { get; init; }
    public required string Series { get; init; }
    public DateTimeOffset IssuedAt { get; init; }
    public required string SupplierTaxId { get; init; }
    public required string SupplierName { get; set; }
    public decimal Total { get; init; }
    public string? XmlObjectKey { get; set; }
    public string? XmlSha256 { get; set; }
    public DateTimeOffset ImportedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? VoidedAt { get; set; }
    public List<SupplierInvoiceItem> Items { get; init; } = [];
}

public sealed class SupplierInvoiceItem : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SupplierInvoiceId { get; init; }
    public Guid InventoryItemId { get; init; }
    public required string SupplierSku { get; init; }
    public string? Barcode { get; init; }
    public required string Name { get; init; }
    public decimal Quantity { get; init; }
    public decimal UnitCost { get; init; }
    public decimal Total { get; init; }
}

public sealed class InventoryMovement : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid InventoryItemId { get; init; }
    public required string Type { get; init; }
    public decimal Quantity { get; init; }
    public decimal UnitCost { get; init; }
    public decimal Total { get; init; }
    public required string EventKey { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public Guid? SupplierInvoiceItemId { get; init; }
    public Guid? MarketplaceOrderItemId { get; init; }
    public Guid? ReversesMovementId { get; init; }
}

public sealed class InventoryReconciliationIssue : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string EventKey { get; init; }
    public required string Type { get; init; }
    public Guid MarketplaceOrderId { get; init; }
    public Guid MarketplaceOrderItemId { get; init; }
    public required string Details { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }
}
