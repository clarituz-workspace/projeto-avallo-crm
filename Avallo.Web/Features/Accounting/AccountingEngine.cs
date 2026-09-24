using Avallo.Connectors.Abstractions;
using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Infrastructure;

namespace Avallo.Web.Features.Accounting;

public sealed class AccountingEngine(AppDbContext db, ITenantContext tenantContext, TimeProvider timeProvider)
{
    public async Task ApplyExpenseApprovalAsync(Expense expense, CancellationToken cancellationToken)
    {
        var eventKey = $"expense:{expense.Id}:approved";
        if (await db.AccountingEntries.AnyAsync(x => x.EventKey == eventKey, cancellationToken))
            return;
        var tenantId = tenantContext.TenantId!.Value;
        var entryId = Guid.NewGuid();
        var (accountCode, accountName) = OperatingExpenseAccount(expense.Category);
        var entry = new AccountingEntry
        {
            Id = entryId,
            TenantId = tenantId,
            EventKey = eventKey,
            Type = AccountingEntryTypes.ExpenseApproval,
            SourceType = "Expense",
            SourceId = expense.Id.ToString(),
            Description = expense.Description,
            OccurredAt = new DateTimeOffset(expense.CompetenceDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
        };
        entry.Postings.Add(Posting(entryId, tenantId, accountCode, accountName, "operacional", expense.Currency, expense.Amount, 0));
        entry.Postings.Add(Posting(entryId, tenantId, AccountingAccounts.OperatingPayable,
            "Despesas operacionais a pagar", "operacional", expense.Currency, 0, expense.Amount));
        db.AccountingEntries.Add(entry);
    }

    public async Task ApplyOrderAsync(
        MarketplaceOrder order,
        StandardOrder source,
        IReadOnlyCollection<MarketplaceFee> fees,
        IReadOnlyCollection<MarketplacePayment> payments,
        CancellationToken cancellationToken)
    {
        var eventPrefix = $"order:{order.Platform}:{order.OrderId}";
        var recognitionKey = $"{eventPrefix}:delivered";
        var recognition = await db.AccountingEntries.Include(x => x.Postings)
            .SingleOrDefaultAsync(x => x.EventKey == recognitionKey, cancellationToken);
        var mustReverse = source.Status == StandardOrderStatus.Cancelled ||
                          source.FulfillmentStatus == StandardFulfillmentStatus.Returned ||
                          payments.Any(x => x.Status == StandardPaymentStatus.Refunded.ToString());

        if (source.FulfillmentStatus == StandardFulfillmentStatus.Delivered && recognition is null && !mustReverse)
        {
            recognition = CreateRecognition(order, source, fees);
            db.AccountingEntries.Add(recognition);
            return;
        }

        if (!mustReverse || recognition is null)
            return;

        var reversalKey = $"{eventPrefix}:reversal";
        if (await db.AccountingEntries.AnyAsync(x => x.EventKey == reversalKey, cancellationToken))
            return;
        db.AccountingEntries.Add(CreateReversal(recognition, reversalKey));
    }

    private AccountingEntry CreateRecognition(
        MarketplaceOrder order,
        StandardOrder source,
        IReadOnlyCollection<MarketplaceFee> fees)
    {
        var tenantId = tenantContext.TenantId!.Value;
        var entryId = Guid.NewGuid();
        var entry = new AccountingEntry
        {
            Id = entryId,
            TenantId = tenantId,
            EventKey = $"order:{order.Platform}:{order.OrderId}:delivered",
            Type = AccountingEntryTypes.DeliveryRecognition,
            SourceType = "MarketplaceOrder",
            SourceId = order.OrderId,
            Description = $"Reconhecimento da entrega {order.OrderId}",
            OccurredAt = source.DeliveredAt ?? timeProvider.GetUtcNow()
        };
        entry.Postings.Add(Posting(entryId, tenantId, AccountingAccounts.MarketplaceReceivable,
            "Contas a receber do marketplace", order.Platform, source.Currency, source.GrossValue, 0));
        entry.Postings.Add(Posting(entryId, tenantId, AccountingAccounts.GrossRevenue,
            "Receita bruta de vendas", order.Platform, source.Currency, 0, source.GrossValue));
        foreach (var fee in fees.Where(x => x.Amount > 0))
        {
            entry.Postings.Add(Posting(entryId, tenantId, ExpenseAccount(fee.Category),
                ExpenseName(fee.Category), order.Platform, fee.Currency, fee.Amount, 0));
            entry.Postings.Add(Posting(entryId, tenantId, AccountingAccounts.MarketplaceReceivable,
                "Contas a receber do marketplace", order.Platform, fee.Currency, 0, fee.Amount));
        }
        return entry;
    }

    private AccountingEntry CreateReversal(AccountingEntry original, string eventKey)
    {
        var tenantId = tenantContext.TenantId!.Value;
        var entryId = Guid.NewGuid();
        var reversal = new AccountingEntry
        {
            Id = entryId,
            TenantId = tenantId,
            EventKey = eventKey,
            Type = AccountingEntryTypes.Reversal,
            SourceType = original.SourceType,
            SourceId = original.SourceId,
            Description = $"Estorno de {original.Description}",
            OccurredAt = timeProvider.GetUtcNow(),
            ReversesEntryId = original.Id
        };
        foreach (var posting in original.Postings)
        {
            var accountCode = posting.AccountCode == AccountingAccounts.GrossRevenue
                ? AccountingAccounts.SalesReturns : posting.AccountCode;
            var accountName = posting.AccountCode == AccountingAccounts.GrossRevenue
                ? "Cancelamentos e devolucoes" : posting.AccountName;
            reversal.Postings.Add(Posting(entryId, tenantId, accountCode, accountName,
                posting.Marketplace, posting.Currency, posting.Credit, posting.Debit));
        }
        return reversal;
    }

    private static AccountingPosting Posting(
        Guid entryId, Guid tenantId, string accountCode, string accountName, string marketplace,
        string currency, decimal debit, decimal credit) => new()
    {
        TenantId = tenantId,
        AccountingEntryId = entryId,
        AccountCode = accountCode,
        AccountName = accountName,
        Marketplace = marketplace,
        Currency = currency,
        Debit = debit,
        Credit = credit
    };

    private static string ExpenseAccount(string category) => category switch
    {
        nameof(StandardFeeCategory.MarketplaceCommission) => AccountingAccounts.MarketplaceCommission,
        nameof(StandardFeeCategory.PaymentProcessing) => AccountingAccounts.PaymentFees,
        nameof(StandardFeeCategory.SellerShipping) => AccountingAccounts.Shipping,
        _ => AccountingAccounts.OtherSellingExpenses
    };

    private static string ExpenseName(string category) => category switch
    {
        nameof(StandardFeeCategory.MarketplaceCommission) => "Comissoes de marketplace",
        nameof(StandardFeeCategory.PaymentProcessing) => "Taxas de pagamento",
        nameof(StandardFeeCategory.SellerShipping) => "Fretes do seller",
        _ => "Outras despesas de venda"
    };

    private static (string Code, string Name) OperatingExpenseAccount(string category) => category switch
    {
        ExpenseCategories.Payroll => (AccountingAccounts.PayrollExpenses, "Folha de pagamento"),
        ExpenseCategories.Rent => (AccountingAccounts.RentExpenses, "Aluguel"),
        ExpenseCategories.Utilities => (AccountingAccounts.UtilitiesExpenses, "Energia e utilidades"),
        ExpenseCategories.Internet => (AccountingAccounts.InternetExpenses, "Internet e telecomunicacoes"),
        ExpenseCategories.Software => (AccountingAccounts.SoftwareExpenses, "Software e assinaturas"),
        ExpenseCategories.ProfessionalServices => (AccountingAccounts.ProfessionalServicesExpenses, "Servicos profissionais"),
        ExpenseCategories.BankFees => (AccountingAccounts.BankExpenses, "Despesas bancarias"),
        _ => (AccountingAccounts.OtherOperatingExpenses, "Outras despesas operacionais")
    };
}
