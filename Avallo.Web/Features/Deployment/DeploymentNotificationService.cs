using Microsoft.AspNetCore.SignalR;

namespace Avallo.Web.Features.Deployment;

public sealed class DeploymentNotificationService(
    IHubContext<DeploymentHub> hubContext,
    TimeProvider timeProvider,
    ILogger<DeploymentNotificationService> logger)
{
    public const string ClientEvent = "DeploymentNotice";

    public async Task<DeploymentNotice> AnnounceAsync(
        DeploymentNoticeRequest request,
        CancellationToken cancellationToken = default)
    {
        var delay = Math.Clamp(request.RestartInSeconds, 15, 600);
        var notice = new DeploymentNotice(
            Guid.NewGuid(),
            request.Version.Trim(),
            string.IsNullOrWhiteSpace(request.Message)
                ? "Versao nova disponivel. Salvando dados e reiniciando em 1 minuto..."
                : request.Message.Trim(),
            timeProvider.GetUtcNow().AddSeconds(delay));

        await hubContext.Clients.All.SendAsync(ClientEvent, notice, cancellationToken);
        logger.LogInformation(
            "Deployment notice {NoticeId} sent for version {Version}; restart scheduled at {RestartAtUtc}.",
            notice.NoticeId,
            notice.Version,
            notice.RestartAtUtc);
        return notice;
    }
}
