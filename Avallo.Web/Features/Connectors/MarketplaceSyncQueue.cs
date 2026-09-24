using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace Avallo.Web.Features.Connectors;

public sealed class ServiceBusOptions
{
    public const string SectionName = "AzureServiceBus";
    public bool Enabled { get; init; }
    public string ConnectionString { get; init; } = string.Empty;
    public string QueueName { get; init; } = "marketplace-sync";
    public int MaxDeliveryCount { get; init; } = 5;
}

public sealed record MarketplaceSyncWorkItem(
    Guid TenantId, Guid ConnectionId, DateTimeOffset Since, Guid LeaseId, string? TriggerId = null);

public sealed class MarketplaceSyncQueue(IOptions<ServiceBusOptions> options) : IAsyncDisposable
{
    private readonly ServiceBusOptions _options = options.Value;
    private ServiceBusClient? _client;
    private ServiceBusSender? _sender;
    public bool IsEnabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.ConnectionString);

    public async Task<bool> EnqueueAsync(MarketplaceSyncWorkItem item, CancellationToken cancellationToken)
    {
        if (!IsEnabled) return false;
        _client ??= new ServiceBusClient(_options.ConnectionString);
        _sender ??= _client.CreateSender(_options.QueueName);
        var message = new ServiceBusMessage(JsonSerializer.Serialize(item))
        {
            ContentType = "application/json",
            Subject = "marketplace-sync",
            MessageId = item.TriggerId ?? $"sync:{item.ConnectionId}:{item.Since:O}"
        };
        await _sender.SendMessageAsync(message, cancellationToken);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_sender is not null) await _sender.DisposeAsync();
        if (_client is not null) await _client.DisposeAsync();
    }
}
