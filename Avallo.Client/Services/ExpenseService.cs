using Microsoft.AspNetCore.Components.Forms;
using Avallo.Client.Models;

namespace Avallo.Client.Services;

public sealed class ExpenseService(AuthService authService)
{
    private const long MaximumFileSize = 10 * 1024 * 1024;

    public Task<ApiResult<ExpenseModel[]>> GetAsync(CancellationToken cancellationToken = default) =>
        authService.GetAsync<ExpenseModel[]>("api/expenses", cancellationToken);

    public Task<ApiResult<ExpenseModel>> CreateAsync(ExpenseRequestModel request, CancellationToken cancellationToken = default) =>
        authService.PostAsync<ExpenseRequestModel, ExpenseModel>("api/expenses", request, cancellationToken);

    public Task<AuthResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        authService.DeleteAsync($"api/expenses/{id}", cancellationToken);

    public Task<ApiResult<ExpenseCategoryModel[]>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        authService.GetAsync<ExpenseCategoryModel[]>("api/expenses/categories", cancellationToken);

    public Task<ApiResult<ExpenseCategoryModel>> CreateCategoryAsync(
        string name, CancellationToken cancellationToken = default) =>
        authService.PostAsync<ExpenseCategoryRequestModel, ExpenseCategoryModel>(
            "api/expenses/categories", new ExpenseCategoryRequestModel(name), cancellationToken);

    public Task<AuthResult> DeleteCategoryAsync(Guid id, CancellationToken cancellationToken = default) =>
        authService.DeleteAsync($"api/expenses/categories/{id}", cancellationToken);

    public Task<ApiResult<ExpenseModel>> SubmitAsync(Guid id, CancellationToken cancellationToken = default) =>
        authService.PostAsync<ExpenseModel>($"api/expenses/{id}/submit", cancellationToken);

    public Task<ApiResult<ExpenseModel>> ApproveAsync(Guid id, CancellationToken cancellationToken = default) =>
        authService.PostAsync<ExpenseModel>($"api/expenses/{id}/approve", cancellationToken);

    public Task<ApiResult<ExpenseModel>> RejectAsync(Guid id, string reason, CancellationToken cancellationToken = default) =>
        authService.PostAsync<object, ExpenseModel>($"api/expenses/{id}/reject", new { reason }, cancellationToken);

    public async Task<ApiResult<ExpenseAttachmentModel>> UploadAsync(
        Guid id, IBrowserFile file, CancellationToken cancellationToken = default)
    {
        if (file.Size is <= 0 or > MaximumFileSize)
            return new ApiResult<ExpenseAttachmentModel>(false, Error: "O comprovante deve ter no maximo 10 MB.");
        await using var source = file.OpenReadStream(MaximumFileSize, cancellationToken);
        using var memory = new MemoryStream();
        await source.CopyToAsync(memory, cancellationToken);
        return await authService.PostFileAsync<ExpenseAttachmentModel>(
            $"api/expenses/{id}/attachments", memory.ToArray(), file.Name,
            file.ContentType ?? "application/octet-stream", cancellationToken);
    }

    public async Task<ApiResult<ExpenseAttachmentModel>> ReplaceAsync(
        Guid expenseId, Guid attachmentId, IBrowserFile file,
        CancellationToken cancellationToken = default)
    {
        if (file.Size is <= 0 or > MaximumFileSize)
            return new ApiResult<ExpenseAttachmentModel>(false, Error: "O comprovante deve ter no maximo 10 MB.");
        await using var source = file.OpenReadStream(MaximumFileSize, cancellationToken);
        using var memory = new MemoryStream();
        await source.CopyToAsync(memory, cancellationToken);
        return await authService.PutFileAsync<ExpenseAttachmentModel>(
            $"api/expenses/{expenseId}/attachments/{attachmentId}", memory.ToArray(), file.Name,
            file.ContentType ?? "application/octet-stream", cancellationToken);
    }

    public Task<ApiResult<ExpenseDownloadModel>> GetDownloadAsync(
        Guid expenseId, Guid attachmentId, CancellationToken cancellationToken = default) =>
        authService.GetAsync<ExpenseDownloadModel>(
            $"api/expenses/{expenseId}/attachments/{attachmentId}/download", cancellationToken);

    public Task<AuthResult> DeleteAttachmentAsync(
        Guid expenseId, Guid attachmentId, CancellationToken cancellationToken = default) =>
        authService.DeleteAsync($"api/expenses/{expenseId}/attachments/{attachmentId}", cancellationToken);
}
