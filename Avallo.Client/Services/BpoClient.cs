using Avallo.Client.Models;

namespace Avallo.Client.Services;

public sealed class BpoClient(AuthService authService)
{
    public Task<ApiResult<BpoDashboardModel>> GetDashboardAsync(CancellationToken cancellationToken = default) =>
        authService.GetAsync<BpoDashboardModel>("api/bpo/dashboard", cancellationToken);

    public Task<ApiResult<BpoBatchResultModel>> ExecuteBatchAsync(
        Guid[] periodIds, string action, CancellationToken cancellationToken = default) =>
        authService.PostAsync<BpoBatchRequestModel, BpoBatchResultModel>(
            "api/bpo/periods/batch", new(periodIds, action), cancellationToken);
}
