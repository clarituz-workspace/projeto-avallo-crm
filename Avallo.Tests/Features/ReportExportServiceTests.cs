using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Features.Reports;
using Avallo.Web.Infrastructure;
using QuestPDF.Infrastructure;
using Xunit;

namespace Avallo.Tests.Features;

public sealed class ReportExportServiceTests
{
    [Theory]
    [InlineData("pdf", "consolidated", "application/pdf", "%PDF")]
    [InlineData("pdf", "platform", "application/pdf", "%PDF")]
    [InlineData("xlsx", "consolidated", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "PK")]
    [InlineData("xlsx", "platform", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "PK")]
    [InlineData("csv", "consolidated", "text/csv; charset=utf-8", "Data;")]
    [InlineData("csv", "platform", "text/csv; charset=utf-8", "Data;")]
    public async Task Export_creates_the_requested_file(
        string format,
        string mode,
        string contentType,
        string expectedSignature)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new StubTenantContext(tenantId);
        await using var context = new AppDbContext(options, tenantContext);
        context.Tenants.Add(new Tenant { Id = tenantId, Name = "Test company" });
        context.FinancialEntries.Add(new FinancialEntry
        {
            TenantId = tenantId,
            ExternalId = "ORDER-1",
            Description = "Test sale",
            Marketplace = "Test marketplace",
            PaymentMethod = "Credit card",
            Status = "Received",
            OccurredAt = DateTimeOffset.UtcNow,
            GrossAmount = 100,
            ReceivedAmount = 90,
            FeeAmount = 10
        });
        context.FinancialEntries.Add(new FinancialEntry
        {
            TenantId = tenantId,
            ExternalId = "ORDER-2",
            Description = "Second sale",
            Marketplace = "Second marketplace",
            PaymentMethod = "Pix",
            Status = "Paid",
            OccurredAt = DateTimeOffset.UtcNow.AddDays(-1),
            GrossAmount = 200,
            ReceivedAmount = 180,
            FeeAmount = 20
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new ReportExportService(context, tenantContext, TimeProvider.System, new ReportExportEngine());

        var report = await service.ExportAsync(
            new ExportReportRequest { Format = format, Mode = mode },
            TestContext.Current.CancellationToken);

        Assert.Equal(contentType, report.ContentType);
        Assert.EndsWith($".{format}", report.FileName);
        if (format == "csv")
            Assert.Contains(expectedSignature, Encoding.UTF8.GetString(report.Content));
        else
            Assert.Equal(expectedSignature, Encoding.ASCII.GetString(report.Content, 0, expectedSignature.Length));

        if (format == "xlsx")
        {
            using var stream = new MemoryStream(report.Content);
            using var workbook = new XLWorkbook(stream);
            Assert.Contains("Consolidado", workbook.Worksheets.Select(x => x.Name));
            Assert.Contains("Test marketplace", workbook.Worksheets.Select(x => x.Name));
            Assert.Contains("Second marketplace", workbook.Worksheets.Select(x => x.Name));
        }
    }

    [Fact]
    public void Period_closing_depends_on_the_shared_export_engine()
    {
        var constructor = typeof(Avallo.Web.Features.PeriodClosing.PeriodClosingService)
            .GetConstructors().Single();

        Assert.Contains(constructor.GetParameters(), parameter =>
            parameter.ParameterType == typeof(IReportExportEngine));
        Assert.DoesNotContain(
            typeof(Avallo.Web.Features.PeriodClosing.PeriodClosingService)
                .GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static),
            method => method.Name.Contains("Pdf", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record StubTenantContext(Guid? TenantId) : ITenantContext;
}
