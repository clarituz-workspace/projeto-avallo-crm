using Avallo.Web.Domain;

namespace Avallo.Web.Features.Reports;

internal static class FinancialEntryQuery
{
    public static IQueryable<FinancialEntry> Apply(
        IQueryable<FinancialEntry> query,
        ReportFilter filter)
    {
        if (filter.From is { } from)
            query = query.Where(x => x.OccurredAt >= new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
        if (filter.To is { } to)
            query = query.Where(x => x.OccurredAt < new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
        if (!string.IsNullOrWhiteSpace(filter.Platform))
            query = query.Where(x => x.Marketplace == filter.Platform);
        if (!string.IsNullOrWhiteSpace(filter.PaymentMethod))
            query = query.Where(x => x.PaymentMethod == filter.PaymentMethod);
        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(x => x.Status == filter.Status);
        return query;
    }
}
