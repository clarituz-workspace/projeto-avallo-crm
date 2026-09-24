using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Avallo.Web.Features.Reports;

/// <summary>
/// Single rendering boundary for every exported report. Feature services build typed documents;
/// only this engine is allowed to turn them into PDF, XLSX or CSV bytes.
/// </summary>
public sealed partial class ReportExportEngine : IReportExportEngine
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public ExportedReport ExportFinancial(
        FinancialReportDocument document,
        string format,
        string mode,
        string baseFileName) =>
        RenderFinancial(document, format, mode, baseFileName);

    public byte[] ExportPeriodClosingPdf(PeriodClosingReportDocument document)
    {
        string Money(decimal value) => value.ToString("C", PtBr);
        return Document.Create(root => root.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9));
            page.Header().Column(column =>
            {
                column.Item().Text("DEMONSTRACAO DO RESULTADO DO EXERCICIO").FontSize(14).Bold().FontColor("#172554");
                column.Item().Text(document.TenantName).FontSize(18).Bold();
                column.Item().Text($"Competencia {document.Month:D2}/{document.Year} | Revisao {document.Revision} | Gerado em {document.GeneratedAt:dd/MM/yyyy HH:mm} UTC")
                    .FontColor("#64748B");
            });
            page.Content().PaddingTop(24).Column(column =>
            {
                column.Spacing(6);
                void Row(string label, decimal value, bool strong = false)
                {
                    column.Item().BorderBottom(1).BorderColor("#E2E8F0").PaddingVertical(6).Row(row =>
                    {
                        var left = row.RelativeItem().Text(label);
                        var right = row.ConstantItem(140).AlignRight().Text(Money(value));
                        if (strong) { left.Bold(); right.Bold(); }
                    });
                }

                Row("Receita bruta", document.Totals.GrossRevenue, true);
                Row("(-) Deducoes", -document.Totals.Deductions);
                Row("(-) Tributos sobre vendas", -document.Totals.Taxes);
                Row("Receita liquida", document.Totals.NetRevenue, true);
                Row("(-) Custo das mercadorias vendidas", -document.Totals.Cmv);
                Row("Lucro bruto", document.Totals.GrossProfit, true);
                Row("(-) Despesas de vendas", -document.Totals.SellingExpense);
                Row("(-) Despesas operacionais", -document.Totals.OperatingExpense);
                Row("RESULTADO DO PERIODO", document.Totals.Result, true);
                column.Item().PaddingTop(20).Text("Saldos por conta").FontSize(12).Bold();
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(65); columns.RelativeColumn();
                        columns.ConstantColumn(90); columns.ConstantColumn(90);
                    });
                    foreach (var header in new[] { "Conta", "Descricao", "Debito", "Credito" })
                        table.Cell().Background("#172554").Padding(5).Text(header).FontColor(Colors.White).Bold();
                    foreach (var account in document.Accounts)
                    {
                        table.Cell().BorderBottom(1).BorderColor("#E2E8F0").Padding(4).Text(account.Code);
                        table.Cell().BorderBottom(1).BorderColor("#E2E8F0").Padding(4).Text(account.Name);
                        table.Cell().BorderBottom(1).BorderColor("#E2E8F0").Padding(4).AlignRight().Text(Money(account.Debit));
                        table.Cell().BorderBottom(1).BorderColor("#E2E8F0").Padding(4).AlignRight().Text(Money(account.Credit));
                    }
                });
            });
            page.Footer().DefaultTextStyle(x => x.FontColor("#64748B")).AlignCenter().Text(text =>
            {
                text.Span("Documento contabil imutavel | ");
                text.CurrentPageNumber(); text.Span(" / "); text.TotalPages();
            });
        })).GeneratePdf();
    }
}
