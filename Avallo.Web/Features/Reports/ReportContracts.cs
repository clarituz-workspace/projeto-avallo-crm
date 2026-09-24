using Microsoft.AspNetCore.Mvc;

namespace Avallo.Web.Features.Reports;

public class ReportFilter
{
    [FromQuery(Name = "from")] public DateOnly? From { get; init; }
    [FromQuery(Name = "to")] public DateOnly? To { get; init; }
    [FromQuery] public string? Platform { get; init; }
    [FromQuery] public string? PaymentMethod { get; init; }
    [FromQuery] public string? Status { get; init; }
}

public sealed class EntriesQuery : ReportFilter
{
    [FromQuery] public string? Search { get; init; }
    [FromQuery] public string SortBy { get; init; } = "date";
    [FromQuery] public bool Descending { get; init; } = true;
    [FromQuery] public int Page { get; init; } = 1;
    [FromQuery] public int PageSize { get; init; } = 10;
    [FromQuery] public string? Cursor { get; init; }
}

public sealed record ReportSummary(decimal Billed, decimal Received, decimal Fees, decimal Receivable);
public sealed record MonthlyEvolution(string Month, decimal Billed, decimal Received);
public sealed record PlatformComparison(string Platform, decimal Billed, decimal Received, decimal Fees);
public sealed record DashboardReport(
    ReportSummary Summary,
    IReadOnlyCollection<MonthlyEvolution> MonthlyEvolution,
    IReadOnlyCollection<PlatformComparison> PlatformComparison);
public sealed record ReportOptions(
    IReadOnlyCollection<string> Platforms,
    IReadOnlyCollection<string> PaymentMethods,
    IReadOnlyCollection<string> Statuses);
public sealed record EntryRow(
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
public sealed record PagedEntries(
    IReadOnlyCollection<EntryRow> Items,
    int Total,
    int Page,
    int PageSize,
    string? NextCursor = null);
