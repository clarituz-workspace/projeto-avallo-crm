namespace Avallo.Web.Domain;

public static class NotificationTypes
{
    public const string MonthlyClose = "MonthlyClose";
    public const string MarketplaceRelease = "MarketplaceRelease";
    public const string NewSale = "NewSale";
    public const string WeeklyAccountantReport = "WeeklyAccountantReport";
}

public sealed class UserNotification : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid UserId { get; init; }
    public required string Type { get; init; }
    public required string EventKey { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public string? Link { get; init; }
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }
}

public sealed class NotificationPreference : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid UserId { get; init; }
    public bool MonthlyCloseEmail { get; set; } = true;
    public bool MarketplaceReleaseAlert { get; set; } = true;
    public bool NewSaleNotification { get; set; }
    public bool WeeklyAccountantReport { get; set; } = true;
    public int MarketplaceReleaseAlertDays { get; set; } = 2;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class EmailOutbox : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid UserId { get; init; }
    public required string EventKey { get; init; }
    public required string Recipient { get; init; }
    public required string Subject { get; init; }
    public required string HtmlBody { get; init; }
    public string? AttachmentName { get; init; }
    public string? AttachmentContentType { get; init; }
    public string? AttachmentObjectKey { get; init; }
    public byte[]? AttachmentContent { get; init; }
    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; set; }
    public string? LastError { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
