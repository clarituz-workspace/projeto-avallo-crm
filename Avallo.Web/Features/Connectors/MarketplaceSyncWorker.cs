using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Avallo.Web.Infrastructure;

namespace Avallo.Web.Features.Connectors;

public sealed class MarketplaceSyncWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ServiceBusOptions> options,
    ILogger<MarketplaceSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = options.Value;
        if (!config.Enabled || string.IsNullOrWhiteSpace(config.ConnectionString))
        {
            logger.LogInformation("Marketplace sync worker is disabled; no queue processor was started.");
            return;
        }
        await using var client = new ServiceBusClient(config.ConnectionString);
        await using var processor = client.CreateProcessor(config.QueueName, new ServiceBusProcessorOptions { MaxConcurrentCalls = 4, AutoCompleteMessages = false, MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(30) });
        processor.ProcessMessageAsync += ProcessAsync;
        processor.ProcessErrorAsync += args => { logger.LogError(args.Exception, "Marketplace sync queue failed."); return Task.CompletedTask; };
        await processor.StartProcessingAsync(stoppingToken);
        try { await Task.Delay(Timeout.Infinite, stoppingToken); } catch (OperationCanceledException) { }
        await processor.StopProcessingAsync(CancellationToken.None);

        async Task ProcessAsync(ProcessMessageEventArgs args)
        {
            var work = JsonSerializer.Deserialize<MarketplaceSyncWorkItem>(args.Message.Body);
            if (work is null) { await args.DeadLetterMessageAsync(args.Message, "invalid_payload"); return; }
            await using var scope = scopeFactory.CreateAsyncScope();
            using var tenant = scope.ServiceProvider.GetRequiredService<ITenantScope>().BeginScope(work.TenantId);
            var sync = scope.ServiceProvider.GetRequiredService<ConnectorSyncService>();
            var gateway = scope.ServiceProvider.GetRequiredService<ConnectorGateway>();
            try
            {
                await sync.SyncAllAsync(work.ConnectionId, work.Since, args.CancellationToken);
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            }
            finally
            {
                await gateway.ReleaseSyncLeaseAsync(work.ConnectionId, work.LeaseId, CancellationToken.None);
            }
        }
    }
}
