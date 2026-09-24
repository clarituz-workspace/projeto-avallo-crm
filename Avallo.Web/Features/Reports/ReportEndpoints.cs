using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Globalization;
using System.Text.Json;
using Avallo.Web.Domain;
using Avallo.Web.Infrastructure;

namespace Avallo.Web.Features.Reports;

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var reports = endpoints.MapGroup("/api/reports")
            .WithTags("Reports")
            .RequireAuthorization(Policies.TenantMember);

        reports.MapGet("/dashboard", GetDashboardAsync)
            .WithName("GetDashboardReport")
            .WithSummary("Retorna totais e graficos consolidados")
            .Produces<DashboardReport>();
        reports.MapGet("/entries", GetEntriesAsync)
            .WithName("GetFinancialEntries")
            .WithSummary("Lista lancamentos com busca, ordenacao e paginacao")
            .Produces<PagedEntries>()
            .ProducesValidationProblem();
        reports.MapGet("/options", GetOptionsAsync)
            .WithName("GetReportOptions")
            .WithSummary("Retorna as opcoes reais disponiveis para os filtros")
            .Produces<ReportOptions>();
        reports.MapGet("/export", ExportAsync)
            .WithName("ExportFinancialReport")
            .WithSummary("Exporta relatorio consolidado ou por plataforma")
            .WithDescription("Formatos aceitos: pdf, xlsx e csv. Modos aceitos: consolidated e platform.")
            .Produces(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> GetDashboardAsync(
        [AsParameters] ReportFilter filter,
        AppDbContext db,
        IDistributedCache cache,
        ReportCacheLock cacheLock,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        if (ValidatePeriod(filter) is { } problem)
            return problem;
        var cacheKey = $"dashboard:{tenantContext.TenantId}:{filter.From:yyyy-MM-dd}:{filter.To:yyyy-MM-dd}:{filter.Platform}:{filter.PaymentMethod}:{filter.Status}";
        var cached = await cache.GetStringAsync(cacheKey, cancellationToken);
        if (cached is not null)
            return Results.Ok(JsonSerializer.Deserialize<DashboardReport>(cached));

        await using var lockLease = await cacheLock.AcquireAsync(cacheKey, TimeSpan.FromSeconds(30), cancellationToken);
        cached = await cache.GetStringAsync(cacheKey, cancellationToken);
        if (cached is not null)
            return Results.Ok(JsonSerializer.Deserialize<DashboardReport>(cached));

        var query = FinancialEntryQuery.Apply(db.FinancialEntries.AsNoTracking(), filter);
        var summary = await query.GroupBy(_ => 1).Select(group => new ReportSummary(
            group.Sum(x => x.GrossAmount),
            group.Sum(x => x.ReceivedAmount),
            group.Sum(x => x.FeeAmount),
            group.Sum(x => x.GrossAmount - x.FeeAmount - x.ReceivedAmount))).SingleOrDefaultAsync(cancellationToken)
            ?? new ReportSummary(0, 0, 0, 0);

        var monthlyData = await query
            .GroupBy(x => new { x.OccurredAt.Year, x.OccurredAt.Month })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                Billed = group.Sum(x => x.GrossAmount),
                Received = group.Sum(x => x.ReceivedAmount)
            })
            .OrderBy(x => x.Year).ThenBy(x => x.Month)
            .ToListAsync(cancellationToken);
        var monthly = monthlyData.Select(x => new MonthlyEvolution(
            $"{x.Year:D4}-{x.Month:D2}", x.Billed, x.Received)).ToArray();

        var platformData = await query.GroupBy(x => x.Marketplace)
            .Select(group => new
            {
                Platform = group.Key,
                Billed = group.Sum(x => x.GrossAmount),
                Received = group.Sum(x => x.ReceivedAmount),
                Fees = group.Sum(x => x.FeeAmount)
            })
            .OrderByDescending(x => x.Billed)
            .ToListAsync(cancellationToken);
        var platforms = platformData.Select(x => new PlatformComparison(
            x.Platform, x.Billed, x.Received, x.Fees)).ToArray();

        var report = new DashboardReport(summary, monthly, platforms);
        await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(report), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2) }, cancellationToken);
        return Results.Ok(report);
    }

    private static async Task<IResult> GetEntriesAsync(
        [AsParameters] EntriesQuery filter,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (ValidatePeriod(filter) is { } periodProblem)
            return periodProblem;
        if (filter.Page < 1 || filter.PageSize is < 5 or > 100)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["pagination"] = ["Page must be positive and pageSize must be between 5 and 100."]
            });

        var query = FinancialEntryQuery.Apply(db.FinancialEntries.AsNoTracking(), filter);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(x => EF.Functions.ILike(x.Description, $"%{search}%") ||
                                     EF.Functions.ILike(x.ExternalId, $"%{search}%"));
        }

        var sortBy = filter.SortBy?.ToLowerInvariant() ?? "date";
        var isDateSort = sortBy is not ("description" or "platform" or "amount" or "status");
        query = (sortBy, filter.Descending) switch
        {
            ("description", false) => query.OrderBy(x => x.Description).ThenBy(x => x.Id),
            ("description", true) => query.OrderByDescending(x => x.Description).ThenByDescending(x => x.Id),
            ("platform", false) => query.OrderBy(x => x.Marketplace).ThenBy(x => x.Id),
            ("platform", true) => query.OrderByDescending(x => x.Marketplace).ThenByDescending(x => x.Id),
            ("amount", false) => query.OrderBy(x => x.GrossAmount).ThenBy(x => x.Id),
            ("amount", true) => query.OrderByDescending(x => x.GrossAmount).ThenByDescending(x => x.Id),
            ("status", false) => query.OrderBy(x => x.Status).ThenBy(x => x.Id),
            ("status", true) => query.OrderByDescending(x => x.Status).ThenByDescending(x => x.Id),
            (_, false) => query.OrderBy(x => x.OccurredAt).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
        };

        var total = await query.CountAsync(cancellationToken);
        if (isDateSort && !string.IsNullOrWhiteSpace(filter.Cursor))
        {
            if (!TryDecodeCursor(filter.Cursor, out var cursor))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["cursor"] = ["The cursor is invalid."]
                });

            query = filter.Descending
                ? query.Where(x => x.OccurredAt < cursor.OccurredAt ||
                                   x.OccurredAt == cursor.OccurredAt && x.Id.CompareTo(cursor.Id) < 0)
                : query.Where(x => x.OccurredAt > cursor.OccurredAt ||
                                   x.OccurredAt == cursor.OccurredAt && x.Id.CompareTo(cursor.Id) > 0);
        }

        // Page remains supported for older clients; the Dashboard sends Cursor for date sorting.
        var useKeyset = isDateSort && (filter.Page == 1 || !string.IsNullOrWhiteSpace(filter.Cursor));
        var rows = useKeyset
            ? await query.Take(filter.PageSize + 1)
                .Select(x => new EntryRow(
                    x.Id, x.ExternalId, x.Description, x.Marketplace, x.PaymentMethod, x.Status,
                    x.OccurredAt, x.ExpectedAt, x.GrossAmount, x.ReceivedAmount, x.FeeAmount,
                    x.GrossAmount - x.FeeAmount - x.ReceivedAmount))
                .ToListAsync(cancellationToken)
            : await query.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(x => new EntryRow(
                x.Id, x.ExternalId, x.Description, x.Marketplace, x.PaymentMethod, x.Status,
                x.OccurredAt, x.ExpectedAt, x.GrossAmount, x.ReceivedAmount, x.FeeAmount,
                x.GrossAmount - x.FeeAmount - x.ReceivedAmount))
            .ToListAsync(cancellationToken);

        var hasNext = useKeyset && rows.Count > filter.PageSize;
        if (hasNext)
            rows.RemoveAt(rows.Count - 1);
        var nextCursor = hasNext ? EncodeCursor(rows[^1]) : null;
        return Results.Ok(new PagedEntries(rows, total, filter.Page, filter.PageSize, nextCursor));
    }

    private static string EncodeCursor(EntryRow row)
    {
        var value = $"{row.OccurredAt.ToUniversalTime():O}|{row.Id:D}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryDecodeCursor(string value, out (DateTimeOffset OccurredAt, Guid Id) cursor)
    {
        cursor = default;
        if (value.Length > 200)
            return false;
        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4);
            var parts = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded)).Split('|');
            if (parts.Length != 2 ||
                !DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var occurredAt) ||
                !Guid.TryParse(parts[1], out var id) || id == Guid.Empty)
                return false;
            cursor = (occurredAt, id);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static async Task<ReportOptions> GetOptionsAsync(AppDbContext db, IDistributedCache cache, ReportCacheLock cacheLock, ITenantContext tenantContext, CancellationToken cancellationToken)
    {
        var cacheKey = $"report-options:{tenantContext.TenantId}";
        var cached = await cache.GetStringAsync(cacheKey, cancellationToken);
        if (cached is not null)
            return JsonSerializer.Deserialize<ReportOptions>(cached)!;
        await using var lockLease = await cacheLock.AcquireAsync(cacheKey, TimeSpan.FromSeconds(30), cancellationToken);
        cached = await cache.GetStringAsync(cacheKey, cancellationToken);
        if (cached is not null)
            return JsonSerializer.Deserialize<ReportOptions>(cached)!;
        var optionsData = await db.FinancialEntries.AsNoTracking()
            .Select(x => new { x.Marketplace, x.PaymentMethod, x.Status })
            .Distinct()
            .ToListAsync(cancellationToken);

        var platforms = optionsData.Select(x => x.Marketplace).Distinct().OrderBy(x => x).ToArray();
        var payments = optionsData.Select(x => x.PaymentMethod).Distinct().OrderBy(x => x).ToArray();
        var statuses = optionsData.Select(x => x.Status).Distinct().OrderBy(x => x).ToArray();
        var result = new ReportOptions(platforms, payments, statuses);
        await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(result), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) }, cancellationToken);
        return result;
    }

    private static async Task<IResult> ExportAsync(
        [AsParameters] ExportReportRequest request,
        ReportExportService exportService,
        CancellationToken cancellationToken)
    {
        if (ValidatePeriod(request) is { } periodProblem)
            return periodProblem;
        if (request.Format.ToLowerInvariant() is not ("pdf" or "xlsx" or "csv"))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["format"] = ["Format must be pdf, xlsx or csv."]
            });
        if (request.Mode.ToLowerInvariant() is not ("consolidated" or "platform"))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["mode"] = ["Mode must be consolidated or platform."]
            });

        try
        {
            var report = await exportService.ExportAsync(request, cancellationToken);
            return Results.File(report.Content, report.ContentType, report.FileName);
        }
        catch (ExportLimitExceededException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }

    private static IResult? ValidatePeriod(ReportFilter filter)
    {
        if (filter.From is null || filter.To is null || filter.From <= filter.To)
            return null;
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["period"] = ["The start date must be earlier than the end date."]
        });
    }
}
