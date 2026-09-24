using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Avallo.Client.Models;

namespace Avallo.Client.Services;

public sealed class DeploymentRealtimeService(
    NavigationManager navigation,
    AuthService auth) : IAsyncDisposable
{
    private HubConnection? _connection;
    private bool _started;

    public event Action<DeploymentNoticeModel>? NoticeReceived;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started || !OperatingSystem.IsBrowser())
            return;

        _connection = new HubConnectionBuilder()
            .WithUrl(navigation.ToAbsoluteUri("/hubs/deployment"), options =>
            {
                options.AccessTokenProvider = auth.GetAccessTokenAsync;
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
            })
            .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)])
            .Build();

        _connection.On<DeploymentNoticeModel>(
            DeploymentNotificationServiceEvent,
            notice => NoticeReceived?.Invoke(notice));

        await _connection.StartAsync(cancellationToken);
        _started = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }

    private const string DeploymentNotificationServiceEvent = "DeploymentNotice";
}
