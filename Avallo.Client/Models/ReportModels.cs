namespace Avallo.Client.Models;

public sealed record ReportSummaryModel(decimal Billed, decimal Received, decimal Fees, decimal Receivable);
public sealed record MonthlyEvolutionModel(string Month, decimal Billed, decimal Received);
public sealed record PlatformComparisonModel(string Platform, decimal Billed, decimal Received, decimal Fees);
public sealed record DashboardReportModel(
    ReportSummaryModel Summary,
    MonthlyEvolutionModel[] MonthlyEvolution,
    PlatformComparisonModel[] PlatformComparison);
public sealed record ReportOptionsModel(string[] Platforms, string[] PaymentMethods, string[] Statuses);
public sealed record EntryModel(
    Guid Id,
    string ExternalId,
    string Description,
    string Marketplace,
    string PaymentMethod,
    string Status,
    DateTimeOffset OccurredAt,
    DateTimeOffset? ExpectedAt,
    decimal GrossAmount,
    decimal ReceivedAmount,
    decimal FeeAmount,
    decimal ReceivableAmount);
public sealed record PagedEntriesModel(EntryModel[] Items, int Total, int Page, int PageSize, string? NextCursor);

public sealed class ReportFilterModel
{
    public DateTime? From { get; set; } = DateTime.Today.AddDays(-29);
    public DateTime? To { get; set; } = DateTime.Today;
    public string? Platform { get; set; }
    public string? PaymentMethod { get; set; }
    public string? Status { get; set; }
    public string? Search { get; set; }
}
