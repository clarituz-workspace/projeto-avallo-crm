using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Avallo.Web.Domain;
using Avallo.Web.Features.Auth;
using Avallo.Web.Infrastructure;

namespace Avallo.Web.Features.Accounting;

public static class AccountingEndpoints
{
    public static IEndpointRouteBuilder MapAccountingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/accounting/dre", GetPreliminaryDreAsync)
            .WithTags("Accounting")
            .WithName("GetPreliminaryDre")
            .WithSummary("Retorna a DRE preliminar baseada no ledger contabil")
            .RequireAuthorization(Policies.TenantMember)
            .Produces<PreliminaryDre>();
        endpoints.MapGet("/api/accounting/legal-dashboard/{periodId:guid}", GetLegalDashboardAsync)
            .WithTags("Accounting").RequireAuthorization(Policies.CanReviewAccounting);
        endpoints.MapPost("/api/accounting/legal-dashboard/{periodId:guid}/release-withdrawal", ReleaseWithdrawalAsync)
            .WithTags("Accounting").RequireAuthorization(Policies.CanReviewAccounting);
        return endpoints;
    }

    private static Task<AccountantLegalDashboard> GetLegalDashboardAsync(
        Guid periodId, LegalAccountingService service, CancellationToken cancellationToken) =>
        service.GetDashboardAsync(periodId, cancellationToken);

    private static async Task<IResult> ReleaseWithdrawalAsync(
        Guid periodId, ReleaseProfitWithdrawalCommand command, ClaimsPrincipal user,
        LegalAccountingService service, CancellationToken cancellationToken)
    {
        try
        {
            var actor = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Results.Ok(await service.ReleaseWithdrawalAsync(periodId, actor, command, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["withdrawal"] = [exception.Message] });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
    }

    private static async Task<IResult> GetPreliminaryDreAsync(
        [AsParameters] DreRequest request,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (request.From is not null && request.To is not null && request.From > request.To)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["period"] = ["Invalid accounting period."] });

        var entries = db.AccountingEntries.AsNoTracking();
        if (request.From is { } from)
            entries = entries.Where(x => x.OccurredAt >= new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        if (request.To is { } to)
            entries = entries.Where(x => x.OccurredAt < new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

        var query = from posting in db.AccountingPostings.AsNoTracking()
                    join entry in entries on posting.AccountingEntryId equals entry.Id
                    where string.IsNullOrWhiteSpace(request.Platform) || posting.Marketplace == request.Platform
                    select posting;
        var balanceData = await query.GroupBy(x => new { x.AccountCode, x.AccountName })
            .Select(group => new
            {
                group.Key.AccountCode,
                group.Key.AccountName,
                Debit = group.Sum(x => x.Debit),
                Credit = group.Sum(x => x.Credit)
            })
            .OrderBy(x => x.AccountCode)
            .ToArrayAsync(cancellationToken);
        var balances = balanceData.Select(x => new DreAccountBalance(
            x.AccountCode, x.AccountName, x.Debit, x.Credit)).ToArray();

        var grossRevenue = CreditBalance(balances, AccountingAccounts.GrossRevenue);
        var deductions = DebitBalance(balances, AccountingAccounts.SalesReturns);
        var taxesOnSales = DebitBalance(balances, AccountingAccounts.TaxOnSales);
        var commission = DebitBalance(balances, AccountingAccounts.MarketplaceCommission);
        var paymentFees = DebitBalance(balances, AccountingAccounts.PaymentFees);
        var shipping = DebitBalance(balances, AccountingAccounts.Shipping);
        var otherExpenses = DebitBalance(balances, AccountingAccounts.OtherSellingExpenses);
        var costOfGoodsSold = DebitBalance(balances, AccountingAccounts.CostOfGoodsSold);
        var operatingExpenses = balances.Where(x => x.AccountCode.StartsWith("5.") ||
                                                     x.AccountCode == AccountingAccounts.LossExpenses)
            .Sum(x => x.Debit - x.Credit);
        var netRevenue = grossRevenue - deductions - taxesOnSales;
        var grossProfit = netRevenue - costOfGoodsSold;
        var sellingExpenses = commission + paymentFees + shipping + otherExpenses;
        return Results.Ok(new PreliminaryDre(
            request.From, request.To, request.Platform, grossRevenue, deductions, taxesOnSales, netRevenue,
            costOfGoodsSold, grossProfit, commission, paymentFees, shipping, otherExpenses,
            sellingExpenses, operatingExpenses, grossProfit - sellingExpenses - operatingExpenses, balances));
    }

    private static decimal DebitBalance(IEnumerable<DreAccountBalance> balances, string accountCode) =>
        balances.Where(x => x.AccountCode == accountCode).Sum(x => x.Debit - x.Credit);

    private static decimal CreditBalance(IEnumerable<DreAccountBalance> balances, string accountCode) =>
        balances.Where(x => x.AccountCode == accountCode).Sum(x => x.Credit - x.Debit);
}

public sealed class DreRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string? Platform { get; init; }
}

public sealed record PreliminaryDre(
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
    DreAccountBalance[] Accounts);

public sealed record DreAccountBalance(string AccountCode, string AccountName, decimal Debit, decimal Credit);
