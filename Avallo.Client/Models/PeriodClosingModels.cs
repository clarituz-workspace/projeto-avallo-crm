namespace Avallo.Client.Models;

public sealed record AccountingPeriodModel(
    Guid Id, int Year, int Month, DateOnly StartDate, DateOnly EndDate, string Status, int Version,
    DateTimeOffset? ValidatedAt, DateTimeOffset? ApprovedAt, DateTimeOffset? ClosedAt,
    DateTimeOffset? ReopenedAt, string? ReopenReason, PeriodCheckModel[] Checks, DreSnapshotModel[] Snapshots);
public sealed record PeriodCheckModel(
    Guid Id, Guid ValidationRunId, string Code, string Description, bool Passed,
    int BlockerCount, string BlockerDetails, DateTimeOffset CheckedAt);
public sealed record DreSnapshotModel(
    Guid Id, int Revision, string CanonicalJsonSha256, string PdfSha256, DateTimeOffset GeneratedAt,
    decimal GrossRevenue, decimal Deductions, decimal Taxes, decimal NetRevenue, decimal Cmv,
    decimal GrossProfit, decimal SellingExpense, decimal OperatingExpense, decimal Result);
public sealed record PeriodValidationModel(
    AccountingPeriodModel Period, Guid ValidationRunId, PeriodCheckModel[] Checks, bool Passed);
public sealed record SnapshotDownloadModel(string Url);
