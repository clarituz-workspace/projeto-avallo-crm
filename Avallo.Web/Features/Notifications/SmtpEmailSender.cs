using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Avallo.Web.Domain;
using Avallo.Web.Features.Expenses;

namespace Avallo.Web.Features.Notifications;

public sealed class SmtpEmailSender(IOptions<EmailOptions> options, IExpenseStorage storage)
{
    private readonly EmailOptions _options = options.Value;
    public bool IsEnabled => _options.Enabled;

    public async Task SendAsync(EmailOutbox email, CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromEmail));
        message.To.Add(MailboxAddress.Parse(email.Recipient));
        message.Subject = email.Subject;
        var body = new BodyBuilder { HtmlBody = email.HtmlBody };
        var attachment = email.AttachmentObjectKey is { } objectKey
            ? await storage.GetAsync(objectKey, cancellationToken)
            : email.AttachmentContent;
        if (attachment is { Length: > 0 } && email.AttachmentName is not null)
            body.Attachments.Add(email.AttachmentName, attachment,
                ContentType.Parse(email.AttachmentContentType ?? "application/octet-stream"));
        message.Body = body.ToMessageBody();

        using var client = new SmtpClient();
        var security = Enum.TryParse<SecureSocketOptions>(_options.Security, ignoreCase: true, out var configuredSecurity)
            ? configuredSecurity
            : SecureSocketOptions.StartTls;
        await client.ConnectAsync(_options.Host, _options.Port, security, cancellationToken);
        if (!string.IsNullOrWhiteSpace(_options.Username))
            await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
