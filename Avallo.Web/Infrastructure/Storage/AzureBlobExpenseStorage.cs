using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;

namespace Avallo.Web.Features.Expenses;

public interface IExpenseStorage
{
    bool IsEnabled { get; }
    Task PutAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken);
    Task<byte[]?> GetAsync(string objectKey, CancellationToken cancellationToken);
    string CreateDownloadUrl(string objectKey, string fileName);
    string CreateReadUrl(string objectKey);
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);
}

public sealed class AzureBlobExpenseStorage : IExpenseStorage
{
    private readonly ObjectStorageOptions _options;
    private readonly BlobContainerClient? _container;
    public bool IsEnabled => _options.Enabled;

    public AzureBlobExpenseStorage(IOptions<ObjectStorageOptions> options)
    {
        _options = options.Value;
        if (!_options.Enabled)
            return;

        _container = new BlobContainerClient(_options.ConnectionString, _options.ContainerName);
    }

    public async Task PutAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        await _container!.GetBlobClient(objectKey).UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        }, cancellationToken);
    }

    public async Task<byte[]?> GetAsync(string objectKey, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        try
        {
            var response = await _container!.GetBlobClient(objectKey).DownloadContentAsync(cancellationToken);
            return response.Value.Content.ToArray();
        }
        catch (Azure.RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public string CreateDownloadUrl(string objectKey, string fileName)
    {
        EnsureEnabled();
        var blob = _container!.GetBlobClient(objectKey);
        if (!blob.CanGenerateSasUri)
            throw new InvalidOperationException("Azure Blob Storage credentials cannot generate a download URL.");

        var sas = new BlobSasBuilder
        {
            BlobContainerName = _options.ContainerName,
            BlobName = objectKey,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(_options.DownloadUrlMinutes),
            ContentDisposition = $"attachment; filename=\"{fileName.Replace("\"", string.Empty)}\""
        };
        sas.SetPermissions(BlobSasPermissions.Read);
        return blob.GenerateSasUri(sas).ToString();
    }

    public string CreateReadUrl(string objectKey)
    {
        EnsureEnabled();
        var blob = _container!.GetBlobClient(objectKey);
        var sas = new BlobSasBuilder
        {
            BlobContainerName = _options.ContainerName,
            BlobName = objectKey,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(_options.DownloadUrlMinutes),
            ContentDisposition = "inline"
        };
        sas.SetPermissions(BlobSasPermissions.Read);
        return blob.GenerateSasUri(sas).ToString();
    }

    public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        await _container!.DeleteBlobIfExistsAsync(objectKey, DeleteSnapshotsOption.IncludeSnapshots,
            cancellationToken: cancellationToken);
    }

    private void EnsureEnabled()
    {
        if (!_options.Enabled)
            throw new InvalidOperationException("Object storage is not configured.");
    }

}
