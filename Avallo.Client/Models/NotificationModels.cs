namespace Avallo.Client.Models;

public sealed record NotificationModel(
    Guid Id,
    string Type,
    string Title,
    string Message,
    string? Link,
    bool IsRead,
    DateTimeOffset CreatedAt);

public sealed record NotificationListModel(NotificationModel[] Items, int UnreadCount);

public sealed class NotificationPreferenceModel
{
    public bool MonthlyCloseEmail { get; set; } = true;
    public bool MarketplaceReleaseAlert { get; set; } = true;
    public bool NewSaleNotification { get; set; }
    public bool WeeklyAccountantReport { get; set; } = true;
    public int MarketplaceReleaseAlertDays { get; set; } = 2;
}
