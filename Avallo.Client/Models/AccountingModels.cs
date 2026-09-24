namespace Avallo.Client.Models;

public sealed record PreliminaryDreModel(
    DateOnly? From,
    DateOnly? To,
    string? Platform,
    decimal GrossRevenue,
    decimal SalesDeductions,
    decimal TaxesOnSales,
    decimal NetRevenue,
    decimal CostOfGoodsSold,
    decimal GrossProfit,
    decimal MarketplaceCommission,
    decimal PaymentFees,
    decimal ShippingExpenses,
    decimal OtherSellingExpenses,
    decimal SellingExpenses,
    decimal OperatingExpenses,
    decimal PreliminaryProfit,
    DreAccountBalanceModel[] Accounts);

public sealed record DreAccountBalanceModel(
    string AccountCode,
    string AccountName,
    decimal Debit,
    decimal Credit);

public sealed record BalanceSheetAccountModel(string Code, string Name, decimal Balance);
public sealed record BalanceSheetPreviewModel(
    DateOnly AsOf, BalanceSheetAccountModel[] Assets, BalanceSheetAccountModel[] Liabilities,
    decimal TotalAssets, decimal TotalLiabilities, decimal RetainedProfit,
    decimal AuthorizedDistributions, decimal Equity, decimal LiabilitiesAndEquity,
    decimal BalanceDifference, bool IsBalanced);
public sealed record ProfitDistributionAuthorizationModel(
    Guid Id, Guid TenantId, Guid AccountingPeriodId, Guid DreSnapshotId,
    string BeneficiaryName, string BeneficiaryTaxId, decimal Amount, string TaxTreatment,
    bool IrpfExemptionConfirmed, string LegalBasis, Guid AuthorizedByUserId, DateTimeOffset AuthorizedAt);
public sealed record ProfitWithdrawalGateModel(
    bool Released, string Status, decimal AvailableProfit, decimal CashAvailable,
    decimal AuthorizedAmount, string? TaxTreatment, bool IrpfExemptionConfirmed,
    string LegalNotice, ProfitDistributionAuthorizationModel[] Authorizations);
public sealed record AccountantLegalDashboardModel(
    Guid PeriodId, string PeriodStatus, BalanceSheetPreviewModel BalanceSheet,
    ProfitWithdrawalGateModel WithdrawalGate);
public sealed record ReleaseProfitWithdrawalModel(
    decimal Amount, string BeneficiaryName, string BeneficiaryTaxId,
    string TaxTreatment, bool IrpfExemptionConfirmed, string LegalBasis);
