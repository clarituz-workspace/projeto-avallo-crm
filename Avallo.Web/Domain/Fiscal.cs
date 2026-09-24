namespace Avallo.Web.Domain;

public enum TaxRegime
{
    SimplesNacional,
    LucroPresumido,
    LucroReal
}

public static class TaxRuleStatuses
{
    public const string Draft = "Draft";
    public const string PendingReview = "PendingReview";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public static class TaxAssessmentTypes
{
    public const string Assessment = "Assessment";
    public const string Reversal = "Reversal";
}

public static class TaxReconciliationIssueTypes
{
    public const string MissingProfile = "MissingProfile";
    public const string NoApprovedRule = "NoApprovedRule";
}

public sealed class TaxProfile : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public int Version { get; init; }
    public DateTimeOffset EffectiveFrom { get; init; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public required string Cnpj { get; init; }
    public required string LegalName { get; init; }
    public string? TradeName { get; init; }
    public required string RegistrationStatus { get; init; }
    public required string CompanySize { get; init; }
    public required string AddressSummary { get; init; }
    public required string MainCnaeCode { get; init; }
    public required string MainCnaeDescription { get; init; }
    public TaxRegime TaxRegime { get; init; }
    public DateTimeOffset SourceLookedUpAt { get; init; }
    public List<TaxProfileSecondaryCnae> SecondaryCnaes { get; init; } = [];
}

public sealed class TaxProfileSecondaryCnae : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid TaxProfileId { get; init; }
    public required string Code { get; init; }
    public required string Description { get; init; }
}

public sealed class TaxRule : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid TaxProfileId { get; init; }
    public int Version { get; init; }
    public DateTimeOffset EffectiveFrom { get; init; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public required string TaxCode { get; init; }
    public required string TaxName { get; init; }
    public decimal Rate { get; init; }
    public string Status { get; set; } = TaxRuleStatuses.Draft;
    public Guid CreatedByUserId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public Guid? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNotes { get; set; }
}

public sealed class TaxAssessment : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid MarketplaceOrderId { get; init; }
    public Guid TaxRuleId { get; init; }
    public string Type { get; init; } = TaxAssessmentTypes.Assessment;
    public decimal TaxableBase { get; init; }
    public decimal Rate { get; init; }
    public decimal TaxAmount { get; init; }
    public DateTimeOffset AssessedAt { get; init; }
    public Guid? ReversesAssessmentId { get; init; }
}

public sealed class TaxReconciliationIssue : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid MarketplaceOrderId { get; init; }
    public required string EventKey { get; init; }
    public required string Type { get; init; }
    public required string Details { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }
}
