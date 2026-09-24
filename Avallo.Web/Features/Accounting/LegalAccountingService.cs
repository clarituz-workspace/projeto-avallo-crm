using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Infrastructure;

namespace Avallo.Web.Features.Accounting;

public sealed record BalanceSheetAccount(string Code, string Name, decimal Balance);
public sealed record BalanceSheetPreview(
    DateOnly AsOf, BalanceSheetAccount[] Assets, BalanceSheetAccount[] Liabilities,
    decimal TotalAssets, decimal TotalLiabilities, decimal RetainedProfit,
    decimal AuthorizedDistributions, decimal Equity, decimal LiabilitiesAndEquity,
    decimal BalanceDifference, bool IsBalanced);
public sealed record ProfitWithdrawalGate(
    bool Released, string Status, decimal AvailableProfit, decimal CashAvailable,
    decimal AuthorizedAmount, string? TaxTreatment, bool IrpfExemptionConfirmed,
    string LegalNotice, ProfitDistributionAuthorization[] Authorizations);
public sealed record AccountantLegalDashboard(
    Guid PeriodId, string PeriodStatus, BalanceSheetPreview BalanceSheet,
    ProfitWithdrawalGate WithdrawalGate);
public sealed record ReleaseProfitWithdrawalCommand(
    decimal Amount, string BeneficiaryName, string BeneficiaryTaxId,
    string TaxTreatment, bool IrpfExemptionConfirmed, string LegalBasis);

public sealed class LegalAccountingService(
    AppDbContext db, ITenantContext tenantContext, TimeProvider timeProvider)
{
    public async Task<AccountantLegalDashboard> GetDashboardAsync(
        Guid periodId, CancellationToken cancellationToken = default)
    {
        _ = tenantContext.TenantId ?? throw new UnauthorizedAccessException("Tenant is required.");
        var period = await db.AccountingPeriods.AsNoTracking().SingleAsync(x => x.Id == periodId, cancellationToken);
        var distributions = await db.ProfitDistributionAuthorizations.AsNoTracking()
            .Where(x => x.AccountingPeriodId == periodId).OrderBy(x => x.AuthorizedAt).ToArrayAsync(cancellationToken);
        var totalAuthorized = await (from authorization in db.ProfitDistributionAuthorizations.AsNoTracking()
                                     join authorizationPeriod in db.AccountingPeriods.AsNoTracking()
                                         on authorization.AccountingPeriodId equals authorizationPeriod.Id
                                     where authorizationPeriod.EndDate <= period.EndDate
                                     select authorization.Amount).SumAsync(cancellationToken);
        var balance = await BuildBalanceSheetAsync(period.EndDate, totalAuthorized, cancellationToken);
        var cash = balance.Assets.Where(x => x.Code == AccountingAccounts.Bank).Sum(x => x.Balance);
        var freeCashAfterLiabilities = Math.Max(0, cash - Math.Max(0, balance.TotalLiabilities));
        var available = Math.Max(0,
            Math.Min(freeCashAfterLiabilities, Math.Max(0, balance.RetainedProfit)) - totalAuthorized);
        var closed = period.Status == AccountingPeriodStatuses.Closed;
        var released = distributions.Length > 0;
        var status = released ? "ReleasedByAccountant" : closed && balance.IsBalanced && available > 0
            ? "AwaitingAccountant" : "Blocked";
        return new AccountantLegalDashboard(period.Id, period.Status, balance,
            new ProfitWithdrawalGate(released, status, available, cash, totalAuthorized,
                distributions.LastOrDefault()?.TaxTreatment,
                distributions.LastOrDefault()?.IrpfExemptionConfirmed ?? false,
                "Estimativa contabil. A liberacao nao substitui deliberacao societaria, escrituracao, EFD-Reinf ou avaliacao tributaria do contador.",
                distributions));
    }

    public async Task<ProfitDistributionAuthorization> ReleaseWithdrawalAsync(
        Guid periodId, Guid accountantUserId, ReleaseProfitWithdrawalCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Amount <= 0) throw new ArgumentOutOfRangeException(nameof(command.Amount), "O valor deve ser positivo.");
        if (string.IsNullOrWhiteSpace(command.BeneficiaryName) || string.IsNullOrWhiteSpace(command.BeneficiaryTaxId))
            throw new ArgumentException("Beneficiario e CPF/CNPJ sao obrigatorios.");
        if (!ProfitDistributionTaxTreatments.All.Contains(command.TaxTreatment, StringComparer.Ordinal))
            throw new ArgumentException("Tratamento tributario invalido.");
        if (string.IsNullOrWhiteSpace(command.LegalBasis) || command.LegalBasis.Trim().Length < 20)
            throw new ArgumentException("Registre a fundamentacao legal e contabil usada pelo contador.");
        if (command.IrpfExemptionConfirmed && command.TaxTreatment != ProfitDistributionTaxTreatments.NoMonthlyWithholding)
            throw new ArgumentException("A confirmacao de isencao e incompatível com retencao informada.");

        var dashboard = await GetDashboardAsync(periodId, cancellationToken);
        if (dashboard.PeriodStatus != AccountingPeriodStatuses.Closed)
            throw new InvalidOperationException("O saque permanece bloqueado ate o fechamento contabil do periodo.");
        if (!dashboard.BalanceSheet.IsBalanced)
            throw new InvalidOperationException("O Balanco Patrimonial possui diferenca e precisa ser corrigido antes da liberacao.");
        if (command.Amount > dashboard.WithdrawalGate.AvailableProfit)
            throw new InvalidOperationException($"O valor excede o lucro disponivel de {dashboard.WithdrawalGate.AvailableProfit:0.00}.");

        var snapshotId = await db.DreSnapshots.Where(x => x.AccountingPeriodId == periodId)
            .OrderByDescending(x => x.Revision).Select(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        if (snapshotId == Guid.Empty)
            throw new InvalidOperationException("O periodo fechado nao possui snapshot contabil.");
        var authorization = new ProfitDistributionAuthorization
        {
            TenantId = tenantContext.TenantId!.Value,
            AccountingPeriodId = periodId,
            DreSnapshotId = snapshotId,
            BeneficiaryName = command.BeneficiaryName.Trim(),
            BeneficiaryTaxId = new string(command.BeneficiaryTaxId.Where(char.IsDigit).ToArray()),
            Amount = decimal.Round(command.Amount, 2),
            TaxTreatment = command.TaxTreatment,
            IrpfExemptionConfirmed = command.IrpfExemptionConfirmed,
            LegalBasis = command.LegalBasis.Trim(),
            AuthorizedByUserId = accountantUserId,
            AuthorizedAt = timeProvider.GetUtcNow()
        };
        db.ProfitDistributionAuthorizations.Add(authorization);
        await db.SaveChangesAsync(cancellationToken);
        return authorization;
    }

    private async Task<BalanceSheetPreview> BuildBalanceSheetAsync(
        DateOnly asOf, decimal distributions, CancellationToken cancellationToken)
    {
        var until = new DateTimeOffset(asOf.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var balances = await (from posting in db.AccountingPostings.AsNoTracking()
                              join entry in db.AccountingEntries.AsNoTracking()
                                  on posting.AccountingEntryId equals entry.Id
                              where entry.OccurredAt < until
                              group posting by new { posting.AccountCode, posting.AccountName } into account
                              select new
                              {
                                  Code = account.Key.AccountCode, Name = account.Key.AccountName,
                                  Debit = account.Sum(x => x.Debit), Credit = account.Sum(x => x.Credit)
                              }).ToArrayAsync(cancellationToken);
        var assets = balances.Where(x => x.Code.StartsWith("1.", StringComparison.Ordinal))
            .Select(x => new BalanceSheetAccount(x.Code, x.Name, x.Debit - x.Credit))
            .OrderBy(x => x.Code).ToArray();
        var liabilities = balances.Where(x => x.Code.StartsWith("2.", StringComparison.Ordinal))
            .Select(x => new BalanceSheetAccount(x.Code, x.Name, x.Credit - x.Debit))
            .OrderBy(x => x.Code).ToArray();
        var retainedProfit = balances.Where(x => x.Code.StartsWith("3.", StringComparison.Ordinal))
                                 .Sum(x => x.Credit - x.Debit)
                             - balances.Where(x => x.Code.StartsWith("4.", StringComparison.Ordinal) ||
                                                   x.Code.StartsWith("5.", StringComparison.Ordinal))
                                 .Sum(x => x.Debit - x.Credit);
        var totalAssets = assets.Sum(x => x.Balance);
        var totalLiabilities = liabilities.Sum(x => x.Balance);
        // An authorization is a gate, not a cash/accounting posting. Equity changes only
        // when the distribution is actually posted to the ledger.
        var equity = retainedProfit;
        var liabilitiesAndEquity = totalLiabilities + equity;
        var difference = totalAssets - liabilitiesAndEquity;
        return new BalanceSheetPreview(asOf, assets, liabilities, totalAssets, totalLiabilities,
            retainedProfit, distributions, equity, liabilitiesAndEquity, difference,
            Math.Abs(difference) <= 0.01m);
    }
}
