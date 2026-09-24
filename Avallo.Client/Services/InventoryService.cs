using Microsoft.AspNetCore.Components.Forms;
using Avallo.Client.Models;

namespace Avallo.Client.Services;

public sealed class InventoryService(AuthService authService)
{
    private const long MaximumXmlSize = 10 * 1024 * 1024;

    public Task<ApiResult<InventoryOverviewModel>> GetOverviewAsync(CancellationToken cancellationToken = default) =>
        authService.GetAsync<InventoryOverviewModel>("api/inventory/overview", cancellationToken);

    public async Task<ApiResult<SupplierInvoiceModel>> ImportInvoiceAsync(
        IBrowserFile file, CancellationToken cancellationToken = default)
    {
        if (file.Size is <= 0 or > MaximumXmlSize)
            return new ApiResult<SupplierInvoiceModel>(false, Error: "O XML deve ter no maximo 10 MB.");
        await using var source = file.OpenReadStream(MaximumXmlSize, cancellationToken);
        using var memory = new MemoryStream();
        await source.CopyToAsync(memory, cancellationToken);
        return await authService.PostFileAsync<SupplierInvoiceModel>(
            "api/inventory/invoices/import", memory.ToArray(), file.Name, "application/xml", cancellationToken);
    }

    public async Task<ApiResult<SupplierInvoicePreviewModel>> PreviewInvoiceAsync(
        IBrowserFile file, CancellationToken cancellationToken = default)
    {
        if (file.Size is <= 0 or > MaximumXmlSize)
            return new ApiResult<SupplierInvoicePreviewModel>(false, Error: "O XML deve ter no maximo 10 MB.");
        await using var source = file.OpenReadStream(MaximumXmlSize, cancellationToken);
        using var memory = new MemoryStream();
        await source.CopyToAsync(memory, cancellationToken);
        return await authService.PostFileAsync<SupplierInvoicePreviewModel>(
            "api/inventory/invoices/preview", memory.ToArray(), file.Name, "application/xml", cancellationToken);
    }

    public Task<ApiResult<InventoryItemModel>> CreateItemAsync(
        CreateInventoryItemModel model, CancellationToken cancellationToken = default) =>
        authService.PostAsync<CreateInventoryItemModel, InventoryItemModel>(
            "api/inventory/items", model, cancellationToken);

    public Task<ApiResult<InventoryItemModel>> UpdateItemAsync(Guid id, UpdateInventoryItemModel model,
        CancellationToken cancellationToken = default) =>
        authService.PutAsync<UpdateInventoryItemModel, InventoryItemModel>($"api/inventory/items/{id}", model, cancellationToken);

    public Task<AuthResult> DeleteItemAsync(Guid id, bool force = false, CancellationToken cancellationToken = default) =>
        authService.DeleteAsync($"api/inventory/items/{id}?force={force.ToString().ToLowerInvariant()}", cancellationToken);

    public Task<ApiResult<SupplierInvoiceDetailModel>> GetInvoiceAsync(Guid id,
        CancellationToken cancellationToken = default) =>
        authService.GetAsync<SupplierInvoiceDetailModel>($"api/inventory/invoices/{id}", cancellationToken);

    public Task<ApiResult<SupplierInvoiceDetailModel>> UpdateInvoiceAsync(Guid id, UpdateSupplierInvoiceModel model,
        CancellationToken cancellationToken = default) =>
        authService.PutAsync<UpdateSupplierInvoiceModel, SupplierInvoiceDetailModel>(
            $"api/inventory/invoices/{id}", model, cancellationToken);

    public Task<AuthResult> DeleteInvoiceAsync(Guid id, CancellationToken cancellationToken = default) =>
        authService.DeleteAsync($"api/inventory/invoices/{id}", cancellationToken);

    public Task<ApiResult<SkuMappingModel>> CreateMappingAsync(
        string platform, string externalSku, Guid inventoryItemId,
        CancellationToken cancellationToken = default) =>
        authService.PostAsync<object, SkuMappingModel>("api/inventory/mappings",
            new { platform, externalSku, inventoryItemId }, cancellationToken);

    public Task<ApiResult<ReprocessInventoryModel>> ReprocessAsync(CancellationToken cancellationToken = default) =>
        authService.PostAsync<ReprocessInventoryModel>("api/inventory/reprocess", cancellationToken);
}
