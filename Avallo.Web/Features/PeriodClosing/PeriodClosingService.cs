using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Features.Expenses;
using Avallo.Web.Features.Reports;
using Avallo.Web.Infrastructure;

namespace Avallo.Web.Features.PeriodClosing;

public static class PeriodCheckCodes
{
    public const string InventoryReconciliation = "INVENTORY_RECONCILIATION";
    public const string MissingSaleCogs = "MISSING_SALE_COGS";
    public const string StockAndSku = "STOCK_AND_SKU";
    public const string Expenses = "EXPENSES";
    public const string Taxes = "TAXES";
    public const string FinancialReconciliation = "FINANCIAL_RECONCILIATION";
    public const string JournalBalance = "JOURNAL_BALANCE";
}

public sealed class PeriodClosingService(
    AppDbContext db,
    ITenantContext tenantContext,
    IExpenseStorage storage,
    TimeProvider timeProvider,
    IReportExportEngine exportEngine)
{
    public async Task<AccountingPeriod> GetOrCreateAsync(
        int year, int month, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        if (year is < 1 or > 9999 || month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), "A valid year and month are required.");

        var existing = await db.AccountingPeriods.SingleOrDefaultAsync(
            x => x.Year == year && x.Month == month, cancellationToken);
        if (existing is not null)
            return existing;

        var start = new DateOnly(year, month, 1);
        var period = new AccountingPeriod
        {
            TenantId = tenantId,
            Year = year,
            Month = month,
            StartDate = start,
            EndDate = start.AddMonths(1).AddDays(-1),
            UpdatedAt = timeProvider.GetUtcNow()
        };
        db.AccountingPeriods.Add(period);
        return period;
    }

    public async Task<PeriodValidationResult> ValidateAsync(
        int year, int month, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var period = await GetOrCreateAsync(year, month, cancellationToken);
        return await ValidatePeriodAsync(period, actorUserId, cancellationToken);
    }

    public async Task<PeriodValidationResult> ValidateAsync(
        Guid periodId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var period = await db.AccountingPeriods.SingleAsync(x => x.Id == periodId, cancellationToken);
        return await ValidatePeriodAsync(period, actorUserId, cancellationToken);
    }

    public async Task<AccountingPeriod> ApproveAsync(
        Guid periodId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var period = await db.AccountingPeriods.SingleAsync(x => x.Id == periodId, cancellationToken);
        if (period.Status != AccountingPeriodStatuses.PendingAccountant)
            throw new InvalidOperationException("Only a period pending accountant review can be approved.");

        var now = timeProvider.GetUtcNow();
        period.Status = AccountingPeriodStatuses.Approved;
        period.ApprovedByUserId = actorUserId;
        period.ApprovedAt = now;
        period.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return period;
    }

    public async Task<DreSnapshot> CloseAsync(
        Guid periodId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var period = await db.AccountingPeriods.SingleAsync(x => x.Id == periodId, cancellationToken);
        if (period.Status != AccountingPeriodStatuses.Approved)
            throw new InvalidOperationException("Only an approved period can be closed.");

        var revision = await db.DreSnapshots.Where(x => x.AccountingPeriodId == period.Id)
            .Select(x => (int?)x.Revision).MaxAsync(cancellationToken) ?? 0;
        revision++;
        var generatedAt = timeProvider.GetUtcNow();
        var report = await BuildReportAsync(period, revision, cancellationToken);
        var canonicalJson = CreateCanonicalJson(report);
        var canonicalHash = Sha256(Encoding.UTF8.GetBytes(canonicalJson));
        var tenantName = await db.Tenants.Where(x => x.Id == tenantId).Select(x => x.Name)
            .SingleOrDefaultAsync(cancellationToken) ?? "Empresa";
        var pdf = exportEngine.ExportPeriodClosingPdf(new PeriodClosingReportDocument(
            tenantName,
            report.Year,
            report.Month,
            report.Revision,
            generatedAt,
            report.Accounts.Select(x => new PeriodClosingAccountRow(x.Code, x.Name, x.Debit, x.Credit)).ToArray(),
            new PeriodClosingTotals(
                report.Totals.GrossRevenue,
                report.Totals.Deductions,
                report.Totals.Taxes,
                report.Totals.NetRevenue,
                report.Totals.Cmv,
                report.Totals.GrossProfit,
                report.Totals.SellingExpense,
                report.Totals.OperatingExpense,
                report.Totals.Result)));
        var pdfHash = Sha256(pdf);
        var objectKey = $"tenants/{tenantId:N}/accounting/periods/{period.Year:D4}-{period.Month:D2}/{revision}.pdf";

        await using (var content = new MemoryStream(pdf, writable: false))
            await storage.PutAsync(objectKey, content, "application/pdf", cancellationToken);

        var snapshot = new DreSnapshot
        {
            TenantId = tenantId,
            AccountingPeriodId = period.Id,
            Revision = revision,
            CanonicalJson = canonicalJson,
            CanonicalJsonSha256 = canonicalHash,
            PdfObjectKey = objectKey,
            PdfSha256 = pdfHash,
            GeneratedByUserId = actorUserId,
            GeneratedAt = generatedAt,
            GrossRevenue = report.Totals.GrossRevenue,
            Deductions = report.Totals.Deductions,
            Taxes = report.Totals.Taxes,
            NetRevenue = report.Totals.NetRevenue,
            Cmv = report.Totals.Cmv,
            GrossProfit = report.Totals.GrossProfit,
            SellingExpense = report.Totals.SellingExpense,
            OperatingExpense = report.Totals.OperatingExpense,
            Result = report.Totals.Result
        };
        db.DreSnapshots.Add(snapshot);
        period.Status = AccountingPeriodStatuses.Closed;
        period.ClosedByUserId = actorUserId;
        period.ClosedAt = generatedAt;
        period.UpdatedAt = generatedAt;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            try
            {
                await storage.DeleteAsync(objectKey, CancellationToken.None);
            }
            catch
            {
                // Preserve the database exception; orphan cleanup can safely retry by object key.
            }
            throw;
        }

        return snapshot;
    }

    public async Task<AccountingPeriod> ReopenAsync(
        Guid periodId, Guid actorUserId, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reopen reason is required.", nameof(reason));

        var period = await db.AccountingPeriods.SingleAsync(x => x.Id == periodId, cancellationToken);
        if (period.Status is not (AccountingPeriodStatuses.Approved or AccountingPeriodStatuses.Closed))
            throw new InvalidOperationException("Only an approved or closed period can be reopened.");

        var now = timeProvider.GetUtcNow();
        period.Status = AccountingPeriodStatuses.Open;
        period.Version++;
        period.ReopenedByUserId = actorUserId;
        period.ReopenedAt = now;
        period.ReopenReason = reason.Trim();
        period.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return period;
    }

    public async Task<string> CreateSnapshotDownloadUrlAsync(
        Guid snapshotId, CancellationToken cancellationToken = default)
    {
        var snapshot = await db.DreSnapshots.AsNoTracking().SingleAsync(x => x.Id == snapshotId, cancellationToken);
        var period = await db.AccountingPeriods.AsNoTracking()
            .SingleAsync(x => x.Id == snapshot.AccountingPeriodId, cancellationToken);
        return storage.CreateDownloadUrl(snapshot.PdfObjectKey,
            $"dre-{period.Year:D4}-{period.Month:D2}-rev-{snapshot.Revision}.pdf");
    }

    private async Task<PeriodValidationResult> ValidatePeriodAsync(
        AccountingPeriod period, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (period.Status is AccountingPeriodStatuses.Approved or AccountingPeriodStatuses.Closed)
            throw new InvalidOperationException("Approved or closed periods must be reopened before validation.");

        var start = new DateTimeOffset(period.StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(period.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var now = timeProvider.GetUtcNow();
        var runId = Guid.NewGuid();
        var deliveredOrderIds = await db.MarketplaceOrders
            .Where(x => x.DeliveredAt >= start && x.DeliveredAt < end)
            .Select(x => x.Id).ToListAsync(cancellationToken);

        var inventoryIssues = await db.InventoryReconciliationIssues
            .Where(x => deliveredOrderIds.Contains(x.MarketplaceOrderId) && x.ResolvedAt == null)
            .OrderBy(x => x.EventKey).Select(x => $"{x.EventKey}: {x.Details}").ToListAsync(cancellationToken);
        var stockIssues = await db.InventoryReconciliationIssues
            .Where(x => deliveredOrderIds.Contains(x.MarketplaceOrderId) && x.ResolvedAt == null &&
                        (x.Type == InventoryReconciliationIssueTypes.InsufficientStock ||
                         x.Type == InventoryReconciliationIssueTypes.UnresolvedSku))
            .OrderBy(x => x.EventKey).Select(x => $"{x.EventKey}: {x.Details}").ToListAsync(cancellationToken);

        var deliveredItems = await db.MarketplaceOrderItems
            .Where(x => deliveredOrderIds.Contains(x.MarketplaceOrderId))
            .Select(x => new { x.Id, x.Sku, x.Title }).ToListAsync(cancellationToken);
        var costedItemIds = await db.InventoryMovements
            .Where(x => x.Type == InventoryMovementTypes.SaleCogs && x.MarketplaceOrderItemId != null)
            .Select(x => x.MarketplaceOrderItemId!.Value).Distinct().ToListAsync(cancellationToken);
        var missingCogs = deliveredItems.Where(x => !costedItemIds.Contains(x.Id))
            .OrderBy(x => x.Id).Select(x => $"{x.Id}: {x.Sku ?? "sem SKU"} - {x.Title}").ToList();

        var expenses = await db.Expenses.Where(x => x.CompetenceDate >= period.StartDate && x.CompetenceDate <= period.EndDate)
            .Select(x => new { x.Id, x.Description, x.Status, HasAttachment = x.Attachments.Any() })
            .ToListAsync(cancellationToken);
        var invalidExpenses = expenses.Where(x => x.Status != ExpenseStatuses.Approved || !x.HasAttachment)
            .OrderBy(x => x.Id)
            .Select(x => $"{x.Id}: {x.Description} ({(x.Status != ExpenseStatuses.Approved ? x.Status : "sem anexo")})")
            .ToList();

        var taxIssues = await db.TaxReconciliationIssues
            .Where(x => deliveredOrderIds.Contains(x.MarketplaceOrderId) && x.ResolvedAt == null)
            .OrderBy(x => x.EventKey).Select(x => $"{x.EventKey}: {x.Details}").ToListAsync(cancellationToken);
        var assessedOrders = await db.TaxAssessments
            .Where(x => deliveredOrderIds.Contains(x.MarketplaceOrderId) && x.Type == TaxAssessmentTypes.Assessment)
            .Join(db.TaxRules.Where(x => x.Status == TaxRuleStatuses.Approved), x => x.TaxRuleId, x => x.Id,
                (assessment, _) => assessment.MarketplaceOrderId)
            .Distinct().ToListAsync(cancellationToken);
        taxIssues.AddRange(deliveredOrderIds.Except(assessedOrders).OrderBy(x => x)
            .Select(x => $"{x}: pedido sem lancamento tributario com regra aprovada"));

        var reconciliationIssues = await db.ReconciliationTransactions.AsNoTracking()
            .Where(x => x.OccurredAt >= start && x.OccurredAt < end && x.Amount > 0 &&
                        x.Status == ReconciliationTransactionStatuses.Unmatched)
            .OrderBy(x => x.OccurredAt)
            .Select(x => $"{x.ExternalId}: credito de {x.Amount:0.00} sem conciliacao")
            .ToListAsync(cancellationToken);
        var releasedPayments = await (
            from payment in db.MarketplacePayments.AsNoTracking()
            join order in db.MarketplaceOrders.AsNoTracking() on payment.MarketplaceOrderId equals order.Id
            where payment.ReleaseAt >= start && payment.ReleaseAt < end && payment.NetValue > 0 && payment.Status == "Paid"
            select new { payment.Id, payment.PaymentId, payment.NetValue, order.OrderId }).ToListAsync(cancellationToken);
        var releasedPaymentIds = releasedPayments.Select(x => x.Id).ToArray();
        var allocatedByPayment = await db.ReconciliationAllocations.AsNoTracking()
            .Where(x => releasedPaymentIds.Contains(x.MarketplacePaymentId))
            .GroupBy(x => x.MarketplacePaymentId)
            .Select(x => new { PaymentId = x.Key, Amount = x.Sum(a => a.Amount) })
            .ToDictionaryAsync(x => x.PaymentId, x => x.Amount, cancellationToken);
        reconciliationIssues.AddRange(releasedPayments
            .Where(x => x.NetValue - allocatedByPayment.GetValueOrDefault(x.Id) > 0.01m)
            .OrderBy(x => x.PaymentId)
            .Select(x => $"{x.PaymentId}: repasse do pedido {x.OrderId} com saldo nao conciliado de {(x.NetValue - allocatedByPayment.GetValueOrDefault(x.Id)):0.00}"));

        var entries = await db.AccountingEntries.Include(x => x.Postings)
            .Where(x => x.OccurredAt >= start && x.OccurredAt < end).ToListAsync(cancellationToken);
        var unbalanced = entries.Where(x => x.Postings.Sum(p => p.Debit) != x.Postings.Sum(p => p.Credit))
            .OrderBy(x => x.EventKey)
            .Select(x => $"{x.EventKey}: debitos {x.Postings.Sum(p => p.Debit):0.00}, creditos {x.Postings.Sum(p => p.Credit):0.00}")
            .ToList();

        var checks = new[]
        {
            Check(period, runId, PeriodCheckCodes.InventoryReconciliation, "Pendencias de conciliacao de estoque", inventoryIssues, now),
            Check(period, runId, PeriodCheckCodes.MissingSaleCogs, "Itens entregues sem movimento de CMV", missingCogs, now),
            Check(period, runId, PeriodCheckCodes.StockAndSku, "Estoque insuficiente ou SKU nao resolvido", stockIssues, now),
            Check(period, runId, PeriodCheckCodes.Expenses, "Despesas aprovadas e documentadas", invalidExpenses, now),
            Check(period, runId, PeriodCheckCodes.Taxes, "Tributos conciliados com regras aprovadas", taxIssues, now),
            Check(period, runId, PeriodCheckCodes.FinancialReconciliation, "Repasses e creditos bancarios conciliados", reconciliationIssues, now),
            Check(period, runId, PeriodCheckCodes.JournalBalance, "Lancamentos contabeis balanceados", unbalanced, now)
        };
        db.AccountingPeriodChecks.AddRange(checks);
        period.ValidatedByUserId = actorUserId;
        period.ValidatedAt = now;
        period.Status = checks.Any(x => !x.Passed)
            ? AccountingPeriodStatuses.Validating
            : AccountingPeriodStatuses.PendingAccountant;
        period.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return new PeriodValidationResult(period, runId, checks);
    }

    private async Task<DreReport> BuildReportAsync(
        AccountingPeriod period, int revision, CancellationToken cancellationToken)
    {
        var start = new DateTimeOffset(period.StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(period.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var postings = await db.AccountingPostings.Where(x => db.AccountingEntries
                .Where(e => e.OccurredAt >= start && e.OccurredAt < end).Select(e => e.Id).Contains(x.AccountingEntryId))
            .Select(x => new { x.AccountCode, x.AccountName, x.Debit, x.Credit }).ToListAsync(cancellationToken);
        var accounts = postings.GroupBy(x => new { x.AccountCode, x.AccountName })
            .Select(x => new AccountBalance(x.Key.AccountCode, x.Key.AccountName,
                x.Sum(p => p.Debit), x.Sum(p => p.Credit)))
            .OrderBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.Name, StringComparer.Ordinal).ToArray();

        decimal Debit(string code) => accounts.Where(x => x.Code == code).Sum(x => x.Debit - x.Credit);
        decimal Credit(string code) => accounts.Where(x => x.Code == code).Sum(x => x.Credit - x.Debit);
        var gross = Credit(AccountingAccounts.GrossRevenue);
        var deductions = Debit(AccountingAccounts.SalesReturns);
        var taxes = Debit(AccountingAccounts.TaxOnSales);
        var net = gross - deductions - taxes;
        var cmv = Debit(AccountingAccounts.CostOfGoodsSold);
        var selling = accounts.Where(x => x.Code.StartsWith("4.1.", StringComparison.Ordinal)).Sum(x => x.Debit - x.Credit);
        var operating = accounts.Where(x => x.Code.StartsWith("5.1.", StringComparison.Ordinal)).Sum(x => x.Debit - x.Credit);
        var totals = new DreTotals(gross, deductions, taxes, net, cmv, net - cmv, selling, operating,
            net - cmv - selling - operating);
        return new DreReport(period.Year, period.Month, revision, accounts, totals);
    }

    private static AccountingPeriodCheck Check(AccountingPeriod period, Guid runId, string code,
        string description, IReadOnlyCollection<string> blockers, DateTimeOffset checkedAt) => new()
    {
        TenantId = period.TenantId,
        AccountingPeriodId = period.Id,
        ValidationRunId = runId,
        Code = code,
        Description = description,
        Passed = blockers.Count == 0,
        BlockerCount = blockers.Count,
        BlockerDetails = JsonSerializer.Serialize(blockers.OrderBy(x => x, StringComparer.Ordinal)),
        CheckedAt = checkedAt
    };

    private static string CreateCanonicalJson(DreReport report)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("year", report.Year);
            writer.WriteNumber("month", report.Month);
            writer.WriteNumber("revision", report.Revision);
            writer.WriteStartArray("accountBalances");
            foreach (var account in report.Accounts)
            {
                writer.WriteStartObject();
                writer.WriteString("code", account.Code);
                writer.WriteString("name", account.Name);
                writer.WriteNumber("debit", account.Debit);
                writer.WriteNumber("credit", account.Credit);
                writer.WriteNumber("balance", account.Debit - account.Credit);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartObject("totals");
            writer.WriteNumber("grossRevenue", report.Totals.GrossRevenue);
            writer.WriteNumber("deductions", report.Totals.Deductions);
            writer.WriteNumber("taxes", report.Totals.Taxes);
            writer.WriteNumber("netRevenue", report.Totals.NetRevenue);
            writer.WriteNumber("cmv", report.Totals.Cmv);
            writer.WriteNumber("grossProfit", report.Totals.GrossProfit);
            writer.WriteNumber("sellingExpense", report.Totals.SellingExpense);
            writer.WriteNumber("operatingExpense", report.Totals.OperatingExpense);
            writer.WriteNumber("result", report.Totals.Result);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private Guid RequireTenant() => tenantContext.TenantId
        ?? throw new UnauthorizedAccessException("Tenant is required.");

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record AccountBalance(string Code, string Name, decimal Debit, decimal Credit);
    private sealed record DreTotals(decimal GrossRevenue, decimal Deductions, decimal Taxes, decimal NetRevenue,
        decimal Cmv, decimal GrossProfit, decimal SellingExpense, decimal OperatingExpense, decimal Result);
    private sealed record DreReport(int Year, int Month, int Revision, AccountBalance[] Accounts, DreTotals Totals);
}

public sealed record PeriodValidationResult(
    AccountingPeriod Period,
    Guid ValidationRunId,
    IReadOnlyList<AccountingPeriodCheck> Checks)
{
    public bool Passed => Checks.All(x => x.Passed);
}
