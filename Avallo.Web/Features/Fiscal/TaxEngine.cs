using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Infrastructure;

namespace Avallo.Web.Features.Fiscal;

public sealed class TaxEngine(AppDbContext db, ITenantContext tenantContext, TimeProvider timeProvider)
{
    public async Task<TaxProcessingResult> ProcessOrderAsync(
        Guid marketplaceOrderId,
        CancellationToken cancellationToken = default)
    {
        var order = await db.MarketplaceOrders.SingleAsync(x => x.Id == marketplaceOrderId, cancellationToken);
        await PreloadAsync([marketplaceOrderId], cancellationToken);
        var result = ProcessLoadedOrder(order);
        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<int> ProcessOrdersAsync(
        IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0)
            return 0;

        var ordersById = await db.MarketplaceOrders
            .Where(x => orderIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        await PreloadAsync(orderIds, cancellationToken);

        var processed = 0;
        foreach (var orderId in orderIds)
        {
            if (!ordersById.TryGetValue(orderId, out var order))
                throw new InvalidOperationException($"Marketplace order '{orderId}' was not found.");
            ProcessLoadedOrder(order);
            processed++;
        }
        await db.SaveChangesAsync(cancellationToken);
        return processed;
    }

    public async Task<int> ReprocessOpenIssuesAsync(CancellationToken cancellationToken = default)
    {
        var orderIds = await db.TaxReconciliationIssues.Where(x => x.ResolvedAt == null)
            .Select(x => x.MarketplaceOrderId).Distinct().ToListAsync(cancellationToken);
        return await ProcessOrdersAsync(orderIds, cancellationToken);
    }

    // Profiles and rules are few per tenant; assessments and issues are loaded once for the whole order set.
    private async Task PreloadAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken)
    {
        await db.TaxProfiles.ToListAsync(cancellationToken);
        await db.TaxRules.ToListAsync(cancellationToken);
        await db.TaxAssessments.Where(x => orderIds.Contains(x.MarketplaceOrderId))
            .ToListAsync(cancellationToken);
        await db.TaxReconciliationIssues.Where(x => orderIds.Contains(x.MarketplaceOrderId))
            .ToListAsync(cancellationToken);
    }

    private TaxProcessingResult ProcessLoadedOrder(MarketplaceOrder order)
    {
        if (IsCancelledOrReturned(order))
            return ReverseLoadedOrder(order);
        if (!string.Equals(order.FulfillmentStatus, "Delivered", StringComparison.OrdinalIgnoreCase) ||
            order.DeliveredAt is not { } deliveredAt)
            return new TaxProcessingResult(0, 0, null);

        var profile = db.TaxProfiles.Local
            .Where(x => x.EffectiveFrom <= deliveredAt && (x.EffectiveTo == null || x.EffectiveTo > deliveredAt))
            .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Version)
            .FirstOrDefault();
        if (profile is null)
        {
            var issue = AddIssue(order, TaxReconciliationIssueTypes.MissingProfile,
                "No fiscal profile is effective at the order delivery date.");
            return new TaxProcessingResult(0, 0, issue);
        }

        var rules = db.TaxRules.Local
            .Where(x => x.TaxProfileId == profile.Id && x.Status == TaxRuleStatuses.Approved &&
                        x.EffectiveFrom <= deliveredAt && (x.EffectiveTo == null || x.EffectiveTo > deliveredAt))
            .OrderBy(x => x.TaxCode).ThenByDescending(x => x.Version)
            .ToList();
        rules = SelectEffectiveRules(profile, rules, deliveredAt).ToList();
        if (rules.Count == 0)
        {
            var issue = AddIssue(order, TaxReconciliationIssueTypes.NoApprovedRule,
                "No approved tax rule is effective at the order delivery date.");
            ResolveIssue(order.Id, TaxReconciliationIssueTypes.MissingProfile);
            return new TaxProcessingResult(0, 0, issue);
        }

        var simulations = Simulate(order.GrossValue, deliveredAt, profile, rules);
        var created = 0;
        foreach (var simulation in simulations)
        {
            if (db.TaxAssessments.Local.Any(x => x.MarketplaceOrderId == order.Id &&
                    x.TaxRuleId == simulation.TaxRuleId && x.Type == TaxAssessmentTypes.Assessment))
                continue;

            var assessment = new TaxAssessment
            {
                TenantId = RequiredTenantId(),
                MarketplaceOrderId = order.Id,
                TaxRuleId = simulation.TaxRuleId,
                TaxableBase = simulation.TaxableBase,
                Rate = simulation.Rate,
                TaxAmount = simulation.TaxAmount,
                AssessedAt = timeProvider.GetUtcNow()
            };
            db.TaxAssessments.Add(assessment);
            db.AccountingEntries.Add(CreateLedgerEntry(order, assessment, simulation.TaxName));
            created++;
        }
        ResolveIssue(order.Id, TaxReconciliationIssueTypes.MissingProfile);
        ResolveIssue(order.Id, TaxReconciliationIssueTypes.NoApprovedRule);
        return new TaxProcessingResult(created, 0, null);
    }

    private TaxProcessingResult ReverseLoadedOrder(MarketplaceOrder order)
    {
        var originals = db.TaxAssessments.Local
            .Where(x => x.MarketplaceOrderId == order.Id && x.Type == TaxAssessmentTypes.Assessment)
            .ToList();
        var reversed = 0;
        foreach (var original in originals)
        {
            if (db.TaxAssessments.Local.Any(x => x.ReversesAssessmentId == original.Id))
                continue;
            var reversal = new TaxAssessment
            {
                TenantId = RequiredTenantId(),
                MarketplaceOrderId = order.Id,
                TaxRuleId = original.TaxRuleId,
                Type = TaxAssessmentTypes.Reversal,
                TaxableBase = -original.TaxableBase,
                Rate = original.Rate,
                TaxAmount = -original.TaxAmount,
                AssessedAt = timeProvider.GetUtcNow(),
                ReversesAssessmentId = original.Id
            };
            db.TaxAssessments.Add(reversal);
            db.AccountingEntries.Add(CreateReversalLedgerEntry(order, original, reversal));
            reversed++;
        }
        return new TaxProcessingResult(0, reversed, null);
    }

    private TaxReconciliationIssue AddIssue(MarketplaceOrder order, string type, string details)
    {
        var eventKey = $"order:{order.Id}:tax:{type}";
        var existing = db.TaxReconciliationIssues.Local.SingleOrDefault(x => x.EventKey == eventKey);
        if (existing is not null)
            return existing;
        var issue = new TaxReconciliationIssue
        {
            TenantId = RequiredTenantId(),
            MarketplaceOrderId = order.Id,
            EventKey = eventKey,
            Type = type,
            Details = details,
            CreatedAt = timeProvider.GetUtcNow()
        };
        db.TaxReconciliationIssues.Add(issue);
        return issue;
    }

    private void ResolveIssue(Guid orderId, string type)
    {
        var issue = db.TaxReconciliationIssues.Local
            .SingleOrDefault(x => x.MarketplaceOrderId == orderId && x.Type == type && x.ResolvedAt == null);
        if (issue is not null)
            issue.ResolvedAt = timeProvider.GetUtcNow();
    }

    public static IReadOnlyList<TaxSimulationLine> Simulate(
        decimal amount,
        DateTimeOffset occurredAt,
        TaxProfile profile,
        IEnumerable<TaxRule> rules)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Taxable amount cannot be negative.");
        if (profile.EffectiveFrom > occurredAt || profile.EffectiveTo is { } profileEnd && profileEnd <= occurredAt)
            return [];
        return SelectEffectiveRules(profile, rules, occurredAt)
            .Select(rule => new TaxSimulationLine(rule.Id, rule.TaxCode, rule.TaxName, amount, rule.Rate,
                Money(amount * rule.Rate / 100m)))
            .ToList();
    }

    public static IEnumerable<TaxRule> SelectEffectiveRules(
        TaxProfile profile,
        IEnumerable<TaxRule> rules,
        DateTimeOffset occurredAt) => rules
        .Where(x => x.TaxProfileId == profile.Id && x.Status == TaxRuleStatuses.Approved &&
                    x.EffectiveFrom <= occurredAt && (x.EffectiveTo == null || x.EffectiveTo > occurredAt))
        .GroupBy(x => x.TaxCode, StringComparer.OrdinalIgnoreCase)
        .Select(x => x.OrderByDescending(rule => rule.EffectiveFrom).ThenByDescending(rule => rule.Version).First());

    private AccountingEntry CreateLedgerEntry(MarketplaceOrder order, TaxAssessment assessment, string taxName)
    {
        var id = Guid.NewGuid();
        var entry = new AccountingEntry
        {
            Id = id,
            TenantId = RequiredTenantId(),
            EventKey = $"tax-assessment:{assessment.Id}",
            Type = AccountingEntryTypes.TaxAssessment,
            SourceType = nameof(TaxAssessment),
            SourceId = assessment.Id.ToString(),
            Description = $"{taxName} do pedido {order.OrderId}",
            OccurredAt = order.DeliveredAt!.Value
        };
        entry.Postings.Add(Posting(id, order, AccountingAccounts.TaxOnSales, "Impostos sobre vendas", assessment.TaxAmount, 0));
        entry.Postings.Add(Posting(id, order, AccountingAccounts.TaxesPayable, "Impostos a recolher", 0, assessment.TaxAmount));
        return entry;
    }

    private AccountingEntry CreateReversalLedgerEntry(
        MarketplaceOrder order,
        TaxAssessment original,
        TaxAssessment reversal)
    {
        var id = Guid.NewGuid();
        var entry = new AccountingEntry
        {
            Id = id,
            TenantId = RequiredTenantId(),
            EventKey = $"tax-reversal:{original.Id}",
            Type = AccountingEntryTypes.TaxReversal,
            SourceType = nameof(TaxAssessment),
            SourceId = reversal.Id.ToString(),
            Description = $"Estorno de impostos do pedido {order.OrderId}",
            OccurredAt = timeProvider.GetUtcNow()
        };
        entry.Postings.Add(Posting(id, order, AccountingAccounts.TaxesPayable, "Impostos a recolher", original.TaxAmount, 0));
        entry.Postings.Add(Posting(id, order, AccountingAccounts.TaxOnSales, "Deducao de impostos sobre vendas", 0, original.TaxAmount));
        return entry;
    }

    private AccountingPosting Posting(
        Guid entryId,
        MarketplaceOrder order,
        string accountCode,
        string accountName,
        decimal debit,
        decimal credit) => new()
    {
        TenantId = RequiredTenantId(),
        AccountingEntryId = entryId,
        AccountCode = accountCode,
        AccountName = accountName,
        Marketplace = order.Platform,
        Currency = order.Currency,
        Debit = debit,
        Credit = credit
    };

    private Guid RequiredTenantId() => tenantContext.TenantId
        ?? throw new UnauthorizedAccessException("A tenant is required to process taxes.");

    private static bool IsCancelledOrReturned(MarketplaceOrder order) =>
        string.Equals(order.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(order.FulfillmentStatus, "Returned", StringComparison.OrdinalIgnoreCase);

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed record TaxSimulationLine(
    Guid TaxRuleId,
    string TaxCode,
    string TaxName,
    decimal TaxableBase,
    decimal Rate,
    decimal TaxAmount);

public sealed record TaxProcessingResult(
    int AssessmentsCreated,
    int ReversalsCreated,
    TaxReconciliationIssue? Issue);
