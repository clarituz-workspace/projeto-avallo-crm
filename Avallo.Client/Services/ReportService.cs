using System.Globalization;
using Avallo.Client.Models;

namespace Avallo.Client.Services;

public sealed class ReportService(AuthService authService)
{
    public Task<ApiResult<ReportOptionsModel>> GetOptionsAsync(CancellationToken cancellationToken = default) =>
        authService.GetAsync<ReportOptionsModel>("api/reports/options", cancellationToken);

    public Task<ApiResult<DashboardReportModel>> GetDashboardAsync(
        ReportFilterModel filter,
        CancellationToken cancellationToken = default) =>
        authService.GetAsync<DashboardReportModel>($"api/reports/dashboard?{BuildFilter(filter)}", cancellationToken);

    public Task<ApiResult<PagedEntriesModel>> GetEntriesAsync(
        ReportFilterModel filter,
        int page,
        int pageSize,
        string sortBy,
        bool descending,
        CancellationToken cancellationToken = default,
        string? cursor = null)
    {
        var query = BuildFilter(filter);
        query += $"&page={page}&pageSize={pageSize}&sortBy={Uri.EscapeDataString(sortBy)}&descending={descending.ToString().ToLowerInvariant()}";
        if (!string.IsNullOrWhiteSpace(cursor))
            query += $"&cursor={Uri.EscapeDataString(cursor)}";
        if (!string.IsNullOrWhiteSpace(filter.Search))
            query += $"&search={Uri.EscapeDataString(filter.Search.Trim())}";
        return authService.GetAsync<PagedEntriesModel>($"api/reports/entries?{query}", cancellationToken);
    }

    public Task<ApiResult<DownloadedFile>> ExportAsync(
        ReportFilterModel filter,
        string format,
        string mode,
        CancellationToken cancellationToken = default)
    {
        var query = BuildFilter(filter);
        query += $"&format={Uri.EscapeDataString(format)}&mode={Uri.EscapeDataString(mode)}&culture={Uri.EscapeDataString(CultureInfo.CurrentUICulture.Name)}";
        return authService.DownloadAsync($"api/reports/export?{query}", cancellationToken);
    }

    private static string BuildFilter(ReportFilterModel filter)
    {
        var values = new List<string>();
        if (filter.From is { } from)
            values.Add($"from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        if (filter.To is { } to)
            values.Add($"to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        Add(values, "platform", filter.Platform);
        Add(values, "paymentMethod", filter.PaymentMethod);
        Add(values, "status", filter.Status);
        return string.Join('&', values);
    }

    private static void Add(ICollection<string> values, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            values.Add($"{name}={Uri.EscapeDataString(value)}");
    }
}
