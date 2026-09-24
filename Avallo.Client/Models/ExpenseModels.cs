namespace Avallo.Client.Models;

public sealed record ExpenseModel(
    Guid Id, string Description, string Category, string? Supplier, DateOnly CompetenceDate,
    DateOnly? DueDate, decimal Amount, string Currency, string Status, string? Notes,
    string? RejectionReason, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    ExpenseAttachmentModel[] Attachments);
public sealed record ExpenseAttachmentModel(
    Guid Id, string FileName, string ContentType, long Size, string Sha256, DateTimeOffset CreatedAt);
public sealed record ExpenseDownloadModel(string Url);
public sealed record ExpenseCategoryModel(Guid Id, string Name);
public sealed record ExpenseCategoryRequestModel(string Name);
public sealed record ExpenseRequestModel(
    string Description, string Category, string? Supplier, DateOnly CompetenceDate,
    DateOnly? DueDate, decimal Amount, string? Notes);

public sealed class ExpenseFormModel
{
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Other";
    public string? Supplier { get; set; }
    public DateTime? CompetenceDate { get; set; } = DateTime.Today;
    public DateTime? DueDate { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}
