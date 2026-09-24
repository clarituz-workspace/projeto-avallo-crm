namespace Avallo.Web.Domain;

public static class ReconciliationSources
{
    public const string Csv = "Csv";
    public const string Ofx = "Ofx";
}

public static class ReconciliationTransactionStatuses
{
    public const string Unmatched = "Unmatched";
    public const string Matched = "Matched";
    public const string Ignored = "Ignored";
}

public static class ReconciliationMatchMethods
{
    public const string ExactReference = "ExactReference";
    public const string AmountAndDate = "AmountAndDate";
    public const string Manual = "Manual";
}

public sealed class ReconciliationImport : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Source { get; init; }
    public required string OriginalFileName { get; init; }
    public required string ObjectKey { get; init; }
    public required string Sha256 { get; init; }
    public string? AccountReference { get; init; }
    public string Currency { get; init; } = "BRL";
    public DateOnly PeriodStart { get; init; }
    public DateOnly PeriodEnd { get; init; }
    public Guid ImportedByUserId { get; init; }
    public DateTimeOffset ImportedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<ReconciliationTransaction> Transactions { get; init; } = [];
}

public sealed class ReconciliationTransaction : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ReconciliationImportId { get; init; }
    public required string ExternalId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "BRL";
    public required string Description { get; init; }
    public string? Reference { get; init; }
    public string Status { get; set; } = ReconciliationTransactionStatuses.Unmatched;
    public string? ReviewNote { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public List<ReconciliationAllocation> Allocations { get; init; } = [];
}

public sealed class ReconciliationAllocation : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ReconciliationTransactionId { get; init; }
    public Guid MarketplacePaymentId { get; init; }
    public decimal Amount { get; init; }
    public required string MatchMethod { get; init; }
    public Guid ConfirmedByUserId { get; init; }
    public DateTimeOffset ConfirmedAt { get; init; }
    public Guid AccountingEntryId { get; init; }
}
