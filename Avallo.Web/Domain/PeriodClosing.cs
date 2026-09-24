namespace Avallo.Web.Domain;

public static class AccountingPeriodStatuses
{
    public const string Open = "Open";
    public const string Validating = "Validating";
    public const string PendingAccountant = "PendingAccountant";
    public const string Approved = "Approved";
    public const string Closed = "Closed";
}

public sealed class AccountingPeriod : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public int Year { get; init; }
    public int Month { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public string Status { get; set; } = AccountingPeriodStatuses.Open;
    public int Version { get; set; } = 1;
    public Guid? ValidatedByUserId { get; set; }
    public DateTimeOffset? ValidatedAt { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? ClosedByUserId { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? ReopenedByUserId { get; set; }
    public DateTimeOffset? ReopenedAt { get; set; }
    public string? ReopenReason { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<AccountingPeriodCheck> Checks { get; init; } = [];
    public List<DreSnapshot> Snapshots { get; init; } = [];
    public List<ProfitDistributionAuthorization> ProfitDistributions { get; init; } = [];
}

public static class ProfitDistributionTaxTreatments
{
    public const string NoMonthlyWithholding = "NoMonthlyWithholding";
    public const string WithholdTenPercent = "WithholdTenPercent";
    public const string Other = "Other";
    public static readonly string[] All = [NoMonthlyWithholding, WithholdTenPercent, Other];
}

public sealed class ProfitDistributionAuthorization : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid AccountingPeriodId { get; init; }
    public Guid DreSnapshotId { get; init; }
    public required string BeneficiaryName { get; init; }
    public required string BeneficiaryTaxId { get; init; }
    public decimal Amount { get; init; }
    public required string TaxTreatment { get; init; }
    public bool IrpfExemptionConfirmed { get; init; }
    public required string LegalBasis { get; init; }
    public Guid AuthorizedByUserId { get; init; }
    public DateTimeOffset AuthorizedAt { get; init; }
}

public sealed class BpoTenantAssignment : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    // Tenant interno ao qual o operador BPO pertence.
    public Guid TenantId { get; set; }
    public Guid OperatorUserId { get; init; }
    public Guid TargetTenantId { get; init; }
    public Guid AssignedByUserId { get; init; }
    public DateTimeOffset AssignedAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class AccountingPeriodCheck : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid AccountingPeriodId { get; init; }
    public Guid ValidationRunId { get; init; }
    public required string Code { get; init; }
    public required string Description { get; init; }
    public bool Passed { get; init; }
    public int BlockerCount { get; init; }
    public required string BlockerDetails { get; init; }
    public DateTimeOffset CheckedAt { get; init; }
}

public sealed class DreSnapshot : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid AccountingPeriodId { get; init; }
    public int Revision { get; init; }
    public required string CanonicalJson { get; init; }
    public required string CanonicalJsonSha256 { get; init; }
    public required string PdfObjectKey { get; init; }
    public required string PdfSha256 { get; init; }
    public Guid GeneratedByUserId { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public decimal GrossRevenue { get; init; }
    public decimal Deductions { get; init; }
    public decimal Taxes { get; init; }
    public decimal NetRevenue { get; init; }
    public decimal Cmv { get; init; }
    public decimal GrossProfit { get; init; }
    public decimal SellingExpense { get; init; }
    public decimal OperatingExpense { get; init; }
    public decimal Result { get; init; }
}
