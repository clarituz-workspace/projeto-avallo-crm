using Microsoft.AspNetCore.Mvc;

namespace Avallo.Web.Features.Reports;

public sealed class ExportReportRequest : ReportFilter
{
    [FromQuery] public string Format { get; init; } = "pdf";
    [FromQuery] public string Mode { get; init; } = "consolidated";
    [FromQuery] public string Culture { get; init; } = "pt-BR";
}

public sealed record ExportedReport(byte[] Content, string ContentType, string FileName);

public interface IReportExportEngine
{
    ExportedReport ExportFinancial(
        FinancialReportDocument document,
        string format,
        string mode,
        string baseFileName);

    byte[] ExportPeriodClosingPdf(PeriodClosingReportDocument document);
}

public sealed record FinancialReportDocument(
    string TenantName,
    string Period,
    DateTimeOffset GeneratedAt,
    IReadOnlyCollection<FinancialReportRow> Rows,
    string Culture);

public sealed record FinancialReportRow(
    string ExternalId,
    string Description,
    string Marketplace,
    string PaymentMethod,
    string Status,
    DateTimeOffset OccurredAt,
    DateTimeOffset? ExpectedAt,
    decimal GrossAmount,
    decimal ReceivedAmount,
    decimal FeeAmount)
{
    public decimal ReceivableAmount => GrossAmount - FeeAmount - ReceivedAmount;
}

public sealed record PeriodClosingReportDocument(
    string TenantName,
    int Year,
    int Month,
    int Revision,
    DateTimeOffset GeneratedAt,
    IReadOnlyCollection<PeriodClosingAccountRow> Accounts,
    PeriodClosingTotals Totals);

public sealed record PeriodClosingAccountRow(
    string Code,
    string Name,
    decimal Debit,
    decimal Credit);

public sealed record PeriodClosingTotals(
    decimal GrossRevenue,
    decimal Deductions,
    decimal Taxes,
    decimal NetRevenue,
    decimal Cmv,
    decimal GrossProfit,
    decimal SellingExpense,
    decimal OperatingExpense,
    decimal Result);

public sealed class ExportLimitExceededException(int limit)
    : Exception($"The export exceeds the limit of {limit:N0} entries. Reduce the selected period.");
