using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Infrastructure;
using Avallo.Web.Features.Expenses;

namespace Avallo.Web.Features.Notifications;

public sealed class NotificationDispatchService(AppDbContext db, ITenantContext tenantContext, IExpenseStorage storage)
{
    public async Task QueueAsync(
        Guid userId,
        string recipient,
        string type,
        string eventKey,
        string title,
        string message,
        string htmlBody,
        bool sendEmail,
        string? link = "/notifications",
        ExportedAttachment? attachment = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.TenantId ?? throw new UnauthorizedAccessException("Tenant is required.");
        if (await db.Notifications.AnyAsync(x => x.UserId == userId && x.EventKey == eventKey, cancellationToken))
            return;

        db.Notifications.Add(new UserNotification
        {
            TenantId = tenantId,
            UserId = userId,
            Type = type,
            EventKey = eventKey,
            Title = title,
            Message = message,
            Link = link
        });
        if (sendEmail)
        {
            var outboxId = Guid.NewGuid();
            string? objectKey = null;
            if (attachment is not null && storage.IsEnabled)
            {
                objectKey = $"email-attachments/{tenantId}/{outboxId:N}";
                await storage.PutAsync(objectKey, new MemoryStream(attachment.Content, writable: false), attachment.ContentType, cancellationToken);
            }
            db.EmailOutbox.Add(new EmailOutbox
            {
                Id = outboxId,
                TenantId = tenantId,
                UserId = userId,
                EventKey = eventKey,
                Recipient = recipient,
                Subject = title,
                HtmlBody = htmlBody,
                AttachmentName = attachment?.Name,
                AttachmentContentType = attachment?.ContentType,
                AttachmentObjectKey = objectKey,
                AttachmentContent = objectKey is null ? attachment?.Content : null
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record ExportedAttachment(string Name, string ContentType, byte[] Content);
