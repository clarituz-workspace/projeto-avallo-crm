using System.ComponentModel.DataAnnotations;

namespace Avallo.Web.Features.Notifications;

public sealed record NotificationResponse(
    Guid Id,
    string Type,
    string Title,
    string Message,
    string? Link,
    bool IsRead,
    DateTimeOffset CreatedAt);

public sealed record NotificationListResponse(
    IReadOnlyCollection<NotificationResponse> Items,
    int UnreadCount);

public sealed record NotificationPreferenceResponse(
    bool MonthlyCloseEmail,
    bool MarketplaceReleaseAlert,
    bool NewSaleNotification,
    bool WeeklyAccountantReport,
    int MarketplaceReleaseAlertDays);

public sealed record UpdateNotificationPreferenceRequest(
    bool MonthlyCloseEmail,
    bool MarketplaceReleaseAlert,
    bool NewSaleNotification,
    bool WeeklyAccountantReport,
    [property: Range(1, 7)] int MarketplaceReleaseAlertDays);
