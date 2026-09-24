namespace Avallo.Web.Domain;

public static class ExpenseStatuses
{
    public const string Draft = "Draft";
    public const string PendingReview = "PendingReview";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public static class ExpenseCategories
{
    public const string Payroll = "Payroll";
    public const string Rent = "Rent";
    public const string Utilities = "Utilities";
    public const string Internet = "Internet";
    public const string Software = "Software";
    public const string ProfessionalServices = "ProfessionalServices";
    public const string BankFees = "BankFees";
    public const string Other = "Other";

    public static readonly string[] All =
    [Payroll, Rent, Utilities, Internet, Software, ProfessionalServices, BankFees, Other];
}

public sealed class Expense : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Description { get; set; }
    public required string Category { get; set; }
    public string? Supplier { get; set; }
    public DateOnly CompetenceDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "BRL";
    public string Status { get; set; } = ExpenseStatuses.Draft;
    public string? Notes { get; set; }
    public Guid CreatedByUserId { get; init; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ExpenseAttachment> Attachments { get; init; } = [];
}

public sealed class ExpenseAttachment : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ExpenseId { get; init; }
    public required string ObjectKey { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long Size { get; set; }
    public required string Sha256 { get; set; }
    public Guid UploadedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class CustomExpenseCategory : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
