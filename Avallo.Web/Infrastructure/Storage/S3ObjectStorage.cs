using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Avallo.Web.Features.Expenses;

/// <summary>
/// Object storage sobre qualquer API S3-compativel (MinIO local, AWS S3, etc.).
/// Equivale ao AzureBlobExpenseStorage: Put/Get/Delete, download e leitura via URL
/// pre-assinada com expiracao — no S3, o Content-Disposition vai em ResponseHeaderOverrides.
/// </summary>
public sealed class S3ObjectStorage : IExpenseStorage
{
    private readonly ObjectStorageOptions _options;
    private readonly AmazonS3Client? _client;
    public bool IsEnabled => _options.Enabled && _options.IsS3;

    public S3ObjectStorage(IOptions<ObjectStorageOptions> options)
    {
        _options = options.Value;
        if (!IsEnabled)
            return;

        _client = new AmazonS3Client(
            new Amazon.Runtime.BasicAWSCredentials(_options.AccessKey, _options.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = _options.ServiceUrl,
                ForcePathStyle = _options.ForcePathStyle,
                AuthenticationRegion = _options.Region
            });
    }

    internal AmazonS3Client Client => _client
        ?? throw new InvalidOperationException("Object storage is not configured.");
    internal string Bucket => _options.ContainerName;

    public async Task PutAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        await Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = Bucket,
            Key = objectKey,
            InputStream = content,
            ContentType = contentType
        }, cancellationToken);
    }

    public async Task<byte[]?> GetAsync(string objectKey, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        try
        {
            using var response = await Client.GetObjectAsync(Bucket, objectKey, cancellationToken);
            await using var memory = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memory, cancellationToken);
            return memory.ToArray();
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public string CreateDownloadUrl(string objectKey, string fileName) =>
        CreateSignedUrl(objectKey, $"attachment; filename=\"{fileName.Replace("\"", string.Empty)}\"");

    public string CreateReadUrl(string objectKey) =>
        CreateSignedUrl(objectKey, "inline");

    public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        await Client.DeleteObjectAsync(Bucket, objectKey, cancellationToken);
    }

    public async Task EnsureBucketAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
            return;
        try
        {
            await Client.PutBucketAsync(Bucket, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
        {
        }
    }

    internal async Task<IReadOnlyCollection<string>> ListKeysAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var keys = new List<string>();
        string? continuationToken = null;
        do
        {
            var page = await Client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = Bucket,
                Prefix = prefix,
                ContinuationToken = continuationToken
            }, cancellationToken);
            foreach (var item in page.S3Objects ?? [])
                keys.Add(item.Key);
            continuationToken = page.IsTruncated == true ? page.NextContinuationToken : null;
        }
        while (continuationToken is not null);
        return keys;
    }

    private string CreateSignedUrl(string objectKey, string contentDisposition)
    {
        EnsureEnabled();
        var signed = Client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = Bucket,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(_options.DownloadUrlMinutes),
            ResponseHeaderOverrides = new ResponseHeaderOverrides { ContentDisposition = contentDisposition }
        });
        // O SDK assina sempre com https; a assinatura SigV4 nao cobre o scheme, entao
        // alinhamos ao ServiceUrl configurado (MinIO local roda http puro).
        var endpoint = new Uri(_options.ServiceUrl);
        return new UriBuilder(signed) { Scheme = endpoint.Scheme, Port = endpoint.Port }.Uri.ToString();
    }

    private void EnsureEnabled()
    {
        if (!IsEnabled)
            throw new InvalidOperationException("Object storage is not configured.");
    }
}
