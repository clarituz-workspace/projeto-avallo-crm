using System.Globalization;
using Avallo.Client.Models;

namespace Avallo.Client.Services;

public sealed class AccountingService(AuthService authService)
{
    public Task<ApiResult<PreliminaryDreModel>> GetPreliminaryDreAsync(
        DateOnly from,
        DateOnly to,
        string? platform,
        CancellationToken cancellationToken = default)
    {
        var query = $"from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        if (!string.IsNullOrWhiteSpace(platform))
            query += $"&platform={Uri.EscapeDataString(platform)}";
        return authService.GetAsync<PreliminaryDreModel>($"api/accounting/dre?{query}", cancellationToken);
    }

    public Task<ApiResult<AccountantLegalDashboardModel>> GetLegalDashboardAsync(
        Guid periodId, CancellationToken cancellationToken = default) =>
        authService.GetAsync<AccountantLegalDashboardModel>(
            $"api/accounting/legal-dashboard/{periodId}", cancellationToken);

    public Task<ApiResult<ProfitDistributionAuthorizationModel>> ReleaseWithdrawalAsync(
        Guid periodId, ReleaseProfitWithdrawalModel request, CancellationToken cancellationToken = default) =>
        authService.PostAsync<ReleaseProfitWithdrawalModel, ProfitDistributionAuthorizationModel>(
            $"api/accounting/legal-dashboard/{periodId}/release-withdrawal", request, cancellationToken);
}
