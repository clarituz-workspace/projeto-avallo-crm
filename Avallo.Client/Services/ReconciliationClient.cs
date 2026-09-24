using Microsoft.AspNetCore.Components.Forms;
using Avallo.Client.Models;

namespace Avallo.Client.Services;

public sealed class ReconciliationClient(AuthService authService)
{
    private const long MaximumStatementSize = 5 * 1024 * 1024;

    public Task<ApiResult<ReconciliationOverviewModel>> GetOverviewAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        authService.GetAsync<ReconciliationOverviewModel>(
            $"api/reconciliation/overview?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", cancellationToken);

    public async Task<ApiResult<ReconciliationImportModel>> ImportAsync(
        IBrowserFile file, CancellationToken cancellationToken = default)
    {
        if (file.Size is <= 0 or > MaximumStatementSize)
            return new ApiResult<ReconciliationImportModel>(false, Error: "O extrato deve ter no maximo 5 MB.");
        await using var source = file.OpenReadStream(MaximumStatementSize, cancellationToken);
        using var memory = new MemoryStream();
        await source.CopyToAsync(memory, cancellationToken);
        return await authService.PostFileAsync<ReconciliationImportModel>("api/reconciliation/imports",
            memory.ToArray(), file.Name, file.ContentType, cancellationToken);
    }

    public Task<ApiResult<ReconciliationActionModel>> ConfirmAsync(Guid transactionId,
        ReconciliationAllocationInput[] allocations, CancellationToken cancellationToken = default) =>
        authService.PostAsync<object, ReconciliationActionModel>(
            $"api/reconciliation/transactions/{transactionId}/confirm", new { allocations }, cancellationToken);

    public Task<ApiResult<ReconciliationActionModel>> IgnoreAsync(Guid transactionId, string reason,
        CancellationToken cancellationToken = default) =>
        authService.PostAsync<object, ReconciliationActionModel>(
            $"api/reconciliation/transactions/{transactionId}/ignore", new { reason }, cancellationToken);
}
