using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Avallo.Web.Domain;

namespace Avallo.Web.Features.Updates;

public sealed class UpdateHub : Hub
{
    public const string ClientEvent = "OnUpdateReceived";

    [Authorize(Policy = Policies.CanManageUsers)]
    public Task SendUpdateNotification(string version, string releaseNotes)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new HubException("A versão é obrigatória.");

        return Clients.All.SendAsync(
            ClientEvent,
            version.Trim(),
            releaseNotes?.Trim() ?? string.Empty);
    }
}
