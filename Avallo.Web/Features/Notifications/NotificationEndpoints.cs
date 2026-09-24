using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Infrastructure;

namespace Avallo.Web.Features.Notifications;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var notifications = endpoints.MapGroup("/api/notifications")
            .WithTags("Notifications")
            .RequireAuthorization(Policies.TenantMember);
        notifications.MapGet("/", GetAsync).WithName("GetNotifications").WithSummary("Lista as notificacoes do usuario");
        notifications.MapPost("/{id:guid}/read", MarkReadAsync).WithName("ReadNotification").WithSummary("Marca uma notificacao como lida");
        notifications.MapPost("/read-all", MarkAllReadAsync).WithName("ReadAllNotifications").WithSummary("Marca todas como lidas");
        notifications.MapGet("/preferences", GetPreferencesAsync).WithName("GetNotificationPreferences");
        notifications.MapPut("/preferences", UpdatePreferencesAsync).WithName("UpdateNotificationPreferences").ProducesValidationProblem();
        return endpoints;
    }

    private static async Task<NotificationListResponse> GetAsync(
        bool unreadOnly,
        ClaimsPrincipal principal,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = UserId(principal);
        var query = db.Notifications.AsNoTracking().Where(x => x.UserId == userId);
        var unreadCount = await query.CountAsync(x => !x.IsRead, cancellationToken);
        if (unreadOnly)
            query = query.Where(x => !x.IsRead);
        var items = await query.OrderByDescending(x => x.CreatedAt).Take(100)
            .Select(x => new NotificationResponse(x.Id, x.Type, x.Title, x.Message, x.Link, x.IsRead, x.CreatedAt))
            .ToListAsync(cancellationToken);
        return new NotificationListResponse(items, unreadCount);
    }

    private static async Task<IResult> MarkReadAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var notification = await db.Notifications.SingleOrDefaultAsync(
            x => x.Id == id && x.UserId == UserId(principal), cancellationToken);
        if (notification is null)
            return Results.NotFound();
        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }
        return Results.NoContent();
    }

    private static async Task<IResult> MarkAllReadAsync(
        ClaimsPrincipal principal,
        AppDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        await db.Notifications.Where(x => x.UserId == UserId(principal) && !x.IsRead)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.IsRead, true)
                .SetProperty(x => x.ReadAt, timeProvider.GetUtcNow()), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<NotificationPreferenceResponse> GetPreferencesAsync(
        ClaimsPrincipal principal,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var preference = await db.NotificationPreferences.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == UserId(principal), cancellationToken);
        return preference is null
            ? new NotificationPreferenceResponse(true, true, false, true, 2)
            : Map(preference);
    }

    private static async Task<IResult> UpdatePreferencesAsync(
        UpdateNotificationPreferenceRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        ITenantContext tenantContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var validation = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), validation, true))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["marketplaceReleaseAlertDays"] = validation.Select(x => x.ErrorMessage!).ToArray()
            });

        var userId = UserId(principal);
        var preference = await db.NotificationPreferences.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (preference is null)
        {
            preference = new NotificationPreference { TenantId = tenantContext.TenantId!.Value, UserId = userId };
            db.NotificationPreferences.Add(preference);
        }
        preference.MonthlyCloseEmail = request.MonthlyCloseEmail;
        preference.MarketplaceReleaseAlert = request.MarketplaceReleaseAlert;
        preference.NewSaleNotification = request.NewSaleNotification;
        preference.WeeklyAccountantReport = request.WeeklyAccountantReport;
        preference.MarketplaceReleaseAlertDays = request.MarketplaceReleaseAlertDays;
        preference.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(Map(preference));
    }

    private static NotificationPreferenceResponse Map(NotificationPreference preference) => new(
        preference.MonthlyCloseEmail, preference.MarketplaceReleaseAlert,
        preference.NewSaleNotification, preference.WeeklyAccountantReport,
        preference.MarketplaceReleaseAlertDays);

    private static Guid UserId(ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
