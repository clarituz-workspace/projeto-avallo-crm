using Azure.Communication.Email;
using Azure;
using Microsoft.Extensions.Options;
using Avallo.Web.Domain;
using Avallo.Web.Features.Expenses;

namespace Avallo.Web.Features.Notifications;

public sealed class AzureCommunicationEmailSender(IOptions<AzureCommunicationEmailOptions> options, IExpenseStorage storage)
{
    private readonly AzureCommunicationEmailOptions _options = options.Value;
    private EmailClient? _client;

    public bool IsEnabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.ConnectionString) &&
                              !string.IsNullOrWhiteSpace(_options.SenderAddress);

    public async Task SendAsync(EmailOutbox email, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
            throw new InvalidOperationException("Azure Communication Email is not configured.");
        _client ??= new EmailClient(_options.ConnectionString);
        var content = new EmailContent(email.Subject) { Html = email.HtmlBody };
        var message = new EmailMessage(_options.SenderAddress, new EmailRecipients([new EmailAddress(email.Recipient)]), content);
        var attachment = email.AttachmentObjectKey is { } objectKey
            ? await storage.GetAsync(objectKey, cancellationToken)
            : email.AttachmentContent;
        if (attachment is { Length: > 0 } && email.AttachmentName is not null)
            message.Attachments.Add(new EmailAttachment(email.AttachmentName, email.AttachmentContentType ?? "application/octet-stream", BinaryData.FromBytes(attachment)));
        await _client.SendAsync(WaitUntil.Completed, message, cancellationToken);
    }
}
