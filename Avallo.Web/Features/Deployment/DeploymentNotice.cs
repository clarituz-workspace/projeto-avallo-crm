namespace Avallo.Web.Features.Deployment;

public sealed record DeploymentNotice(
    Guid NoticeId,
    string Version,
    string Message,
    DateTimeOffset RestartAtUtc);

public sealed record DeploymentNoticeRequest(
    string Version,
    string? Message = null,
    int RestartInSeconds = 60);
