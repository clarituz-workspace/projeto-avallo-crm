namespace Avallo.Client.Models;

public sealed record DeploymentNoticeModel(
    Guid NoticeId,
    string Version,
    string Message,
    DateTimeOffset RestartAtUtc);
