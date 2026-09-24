namespace Avallo.Client.Models;

public sealed record InventoryOverviewModel(
    InventoryItemModel[] Items,
    InventoryIssueModel[] Issues,
    SupplierInvoiceModel[] Invoices);
public sealed record InventoryItemModel(
    Guid Id, string Sku, string Name, decimal QuantityOnHand, decimal AverageUnitCost,
    decimal InventoryValue, SkuMappingModel[] Mappings);
public sealed record SkuMappingModel(Guid Id, string Platform, string ExternalSku);
public sealed record InventoryIssueModel(
    Guid Id, string Type, string Details, string OrderId, string Platform,
    string? ExternalSku, string ItemName, DateTimeOffset CreatedAt);
public sealed record SupplierInvoiceModel(
    Guid Id, string AccessKey, string InvoiceNumber, string Series, DateTimeOffset IssuedAt,
    string SupplierTaxId, string SupplierName, decimal Total, int ItemCount, DateTimeOffset ImportedAt);
public sealed record SupplierInvoicePreviewModel(
    string AccessKey, string InvoiceNumber, string Series, DateTimeOffset IssuedAt,
    string SupplierTaxId, string SupplierName, decimal Total, bool AlreadyImported,
    SupplierInvoiceItemPreviewModel[] Items);
public sealed record SupplierInvoiceDetailModel(
    Guid Id, string AccessKey, string InvoiceNumber, string Series, DateTimeOffset IssuedAt,
    string SupplierTaxId, string SupplierName, decimal Total, DateTimeOffset ImportedAt,
    SupplierInvoiceItemPreviewModel[] Items);
public sealed record SupplierInvoiceItemPreviewModel(
    string SupplierSku, string? Barcode, string Name, decimal Quantity, decimal UnitCost, decimal Total);
public sealed class CreateInventoryItemModel
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal QuantityOnHand { get; set; }
    public decimal AverageUnitCost { get; set; }
}
public sealed class UpdateInventoryItemModel { public string Name { get; set; } = string.Empty; }
public sealed class UpdateSupplierInvoiceModel { public string SupplierName { get; set; } = string.Empty; }
public sealed record ReprocessInventoryModel(int ProcessedItems);
