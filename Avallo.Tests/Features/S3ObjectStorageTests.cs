using System.Net.Sockets;
using System.Text;
using Avallo.Web.Features.Expenses;
using Microsoft.Extensions.Options;
using Xunit;

namespace Avallo.Tests.Features;

/// <summary>
/// Integracao real contra o MinIO local (compose.local.yml, porta 9000).
/// Os testes pulam quando o MinIO nao esta de pe — rode `.\scripts\setup-local.ps1` antes.
/// </summary>
public sealed class S3ObjectStorageTests
{
    private static bool MinioAvailable()
    {
        try
        {
            using var tcp = new TcpClient();
            tcp.Connect("localhost", 9000);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static S3ObjectStorage CreateStorage() => new(Options.Create(new ObjectStorageOptions
    {
        Enabled = true,
        Provider = "s3",
        ServiceUrl = "http://localhost:9000",
        AccessKey = "avallo",
        SecretKey = "avallo-dev-key",
        ContainerName = "uploads",
        DownloadUrlMinutes = 10
    }));

    [Fact]
    public async Task Put_get_delete_roundtrip()
    {
        Assert.SkipUnless(MinioAvailable(), "MinIO local indisponivel na porta 9000.");
        var storage = CreateStorage();
        await storage.EnsureBucketAsync();
        var key = $"tests/{Guid.NewGuid():N}.txt";
        var payload = Encoding.UTF8.GetBytes("avallo-minio-smoke");

        await storage.PutAsync(key, new MemoryStream(payload), "text/plain", CancellationToken.None);
        var roundtrip = await storage.GetAsync(key, CancellationToken.None);
        Assert.Equal(payload, roundtrip);

        await storage.DeleteAsync(key, CancellationToken.None);
        Assert.Null(await storage.GetAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task Presigned_urls_serve_the_object_with_content_disposition()
    {
        Assert.SkipUnless(MinioAvailable(), "MinIO local indisponivel na porta 9000.");
        var storage = CreateStorage();
        await storage.EnsureBucketAsync();
        var key = $"tests/{Guid.NewGuid():N}.bin";
        var payload = Encoding.UTF8.GetBytes("presigned-content");

        await storage.PutAsync(key, new MemoryStream(payload), "application/octet-stream", CancellationToken.None);
        using var http = new HttpClient();

        var read = await http.GetAsync(storage.CreateReadUrl(key));
        Assert.Equal(payload, await read.Content.ReadAsByteArrayAsync());

        var download = await http.GetAsync(storage.CreateDownloadUrl(key, "arquivo.bin"));
        Assert.Equal(payload, await download.Content.ReadAsByteArrayAsync());
        Assert.Contains("attachment", download.Content.Headers.ContentDisposition?.ToString() ?? "");

        await storage.DeleteAsync(key, CancellationToken.None);
    }

    [Fact]
    public void Presigned_url_uses_the_configured_endpoint()
    {
        var storage = CreateStorage();
        Assert.StartsWith("http://localhost:9000/uploads/", storage.CreateReadUrl("tenants/t/f.pdf"));
    }

    [Fact]
    public async Task Disabled_storage_throws_instead_of_calling_s3()
    {
        var storage = new S3ObjectStorage(Options.Create(new ObjectStorageOptions { Enabled = false, Provider = "s3" }));
        Assert.False(storage.IsEnabled);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.PutAsync("k", Stream.Null, "text/plain", CancellationToken.None));
    }
}
