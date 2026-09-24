using System.ComponentModel.DataAnnotations;

namespace Avallo.Web.Features.Expenses;

public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";
    public bool Enabled { get; init; }

    /// <summary>"azure" usa Azure Blob Storage (producao); "s3" usa qualquer API
    /// S3-compativel — MinIO no ambiente local, S3 real ou outro compativel em outro lugar.</summary>
    public string Provider { get; init; } = "azure";

    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>Endpoint S3-compativel (MinIO local: http://localhost:9000).</summary>
    public string ServiceUrl { get; init; } = string.Empty;
    public string AccessKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public string Region { get; init; } = "us-east-1";

    /// <summary>MinIO exige path-style (host/bucket/chave). S3 da AWS aceita virtual-hosted.</summary>
    public bool ForcePathStyle { get; init; } = true;

    /// <summary>Nome do container (Azure) ou bucket (S3) — mesmo papel nos dois providers.</summary>
    public string ContainerName { get; init; } = "uploads";
    [Range(1, 60)] public int DownloadUrlMinutes { get; init; } = 10;

    public bool IsS3 => string.Equals(Provider, "s3", StringComparison.OrdinalIgnoreCase);
}
