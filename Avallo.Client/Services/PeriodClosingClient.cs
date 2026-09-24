using Avallo.Client.Models;

namespace Avallo.Client.Services;

public sealed class PeriodClosingClient(AuthService authService)
{
    public Task<ApiResult<AccountingPeriodModel[]>> GetAsync(CancellationToken cancellationToken = default) =>
        authService.GetAsync<AccountingPeriodModel[]>("api/accounting-periods", cancellationToken);
    public Task<ApiResult<AccountingPeriodModel>> CreateAsync(int year, int month, CancellationToken cancellationToken = default) =>
        authService.PostAsync<AccountingPeriodModel>($"api/accounting-periods/{year}/{month}", cancellationToken);
    public Task<ApiResult<PeriodValidationModel>> ValidateAsync(Guid id, CancellationToken cancellationToken = default) =>
        authService.PostAsync<PeriodValidationModel>($"api/accounting-periods/{id}/validate", cancellationToken);
    public Task<ApiResult<AccountingPeriodModel>> ApproveAsync(Guid id, CancellationToken cancellationToken = default) =>
        authService.PostAsync<AccountingPeriodModel>($"api/accounting-periods/{id}/approve", cancellationToken);
    public Task<ApiResult<AccountingPeriodModel>> CloseAsync(Guid id, CancellationToken cancellationToken = default) =>
        authService.PostAsync<AccountingPeriodModel>($"api/accounting-periods/{id}/close", cancellationToken);
    public Task<ApiResult<AccountingPeriodModel>> ReopenAsync(Guid id, string reason, CancellationToken cancellationToken = default) =>
        authService.PostAsync<object, AccountingPeriodModel>($"api/accounting-periods/{id}/reopen", new { reason }, cancellationToken);
    public Task<ApiResult<SnapshotDownloadModel>> GetDownloadAsync(
        Guid periodId, Guid snapshotId, CancellationToken cancellationToken = default) =>
        authService.GetAsync<SnapshotDownloadModel>(
            $"api/accounting-periods/{periodId}/snapshots/{snapshotId}/download", cancellationToken);
}
