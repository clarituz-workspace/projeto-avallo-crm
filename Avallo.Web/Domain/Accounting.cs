namespace Avallo.Web.Domain;

public static class AccountingAccounts
{
    public const string Bank = "1.1.01";
    public const string Inventory = "1.1.03";
    public const string MarketplaceReceivable = "1.1.02";
    public const string GrossRevenue = "3.1.01";
    public const string SalesReturns = "3.2.01";
    public const string MarketplaceCommission = "4.1.01";
    public const string PaymentFees = "4.1.02";
    public const string Shipping = "4.1.03";
    public const string OtherSellingExpenses = "4.1.99";
    public const string OperatingPayable = "2.1.01";
    public const string Suppliers = "2.1.02";
    public const string CostOfGoodsSold = "4.2.01";
    public const string LossExpenses = "4.2.02";
    public const string PayrollExpenses = "5.1.01";
    public const string RentExpenses = "5.1.02";
    public const string UtilitiesExpenses = "5.1.03";
    public const string InternetExpenses = "5.1.04";
    public const string SoftwareExpenses = "5.1.05";
    public const string ProfessionalServicesExpenses = "5.1.06";
    public const string BankExpenses = "5.1.07";
    public const string OtherOperatingExpenses = "5.1.99";
    public const string TaxOnSales = "3.2.02";
    public const string TaxesPayable = "2.1.03";
}

public static class AccountingEntryTypes
{
    public const string DeliveryRecognition = "DeliveryRecognition";
    public const string Reversal = "Reversal";
    public const string ExpenseApproval = "ExpenseApproval";
    public const string PurchaseReceipt = "PurchaseReceipt";
    public const string SaleCogs = "SaleCogs";
    public const string SaleReturn = "SaleReturn";
    public const string TaxAssessment = "TaxAssessment";
    public const string TaxReversal = "TaxReversal";
    public const string MarketplaceSettlement = "MarketplaceSettlement";
    public const string InventoryLoss = "InventoryLoss";
}

public sealed class MarketplacePayment : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid MarketplaceOrderId { get; init; }
    public required string PaymentId { get; init; }
    public decimal GrossValue { get; set; }
    public decimal NetValue { get; set; }
    public decimal PaymentFee { get; set; }
    // Split declarado pela plataforma, guardado para conciliacao. O razao usa MarketplaceFees.
    public decimal PlatformFee { get; set; }
    public decimal ShippingCost { get; set; }
    public required string Method { get; set; }
    public required string Status { get; set; }
    public string Currency { get; set; } = "BRL";
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? ReleaseAt { get; set; }
    public DateTimeOffset SyncedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MarketplaceFee : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid MarketplaceOrderId { get; init; }
    public required string ExternalKey { get; init; }
    public required string Type { get; set; }
    public required string Category { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "BRL";
    public DateTimeOffset SyncedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AccountingEntry : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string EventKey { get; init; }
    public required string Type { get; init; }
    public required string SourceType { get; init; }
    public required string SourceId { get; init; }
    public required string Description { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public Guid? ReversesEntryId { get; init; }
    public List<AccountingPosting> Postings { get; init; } = [];
}

public sealed class AccountingPosting : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid AccountingEntryId { get; init; }
    public required string AccountCode { get; init; }
    public required string AccountName { get; init; }
    public required string Marketplace { get; init; }
    public string Currency { get; init; } = "BRL";
    public decimal Debit { get; init; }
    public decimal Credit { get; init; }
}
