using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Avallo.Web.Domain;
using Avallo.Web.Infrastructure;

namespace Avallo.Web.Features.Connectors;

public sealed class MarketplaceSyncSchedulerOptions
{
    public const string SectionName = "MarketplaceSyncScheduler";
    public bool Enabled { get; init; } = true;
    public int IntervalMinutes { get; init; } = 15;
    public int InitialLookbackHours { get; init; } = 24;
    public int OverlapMinutes { get; init; } = 5;
}

/// <summary>
/// Agenda sincronizacoes periodicas das conexoes ativas de marketplace.
/// Cobre o caso em que o webhook nao esta configurado: sem este worker os
/// dados so sincronizariam por webhook ou acao manual. O lease por conexao
/// (SyncLeaseUntil) garante que replicas concorrentes nao disparam o mesmo
/// sync, e o TriggerId deduplica a mensagem na fila dentro do intervalo.
/// </summary>
public sealed class MarketplaceSyncSchedulerWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MarketplaceSyncSchedulerOptions> options,
    TimeProvider timeProvider,
    ILogger<MarketplaceSyncSchedulerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = options.Value;
        if (!config.Enabled)
        {
            logger.LogInformation("Marketplace sync scheduler is disabled.");
            return;
        }
        var interval = TimeSpan.FromMinutes(Math.Max(1, config.IntervalMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScheduleDueSyncsAsync(config, interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Marketplace sync scheduler cycle failed.");
            }
            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task ScheduleDueSyncsAsync(
        MarketplaceSyncSchedulerOptions config, TimeSpan interval, CancellationToken cancellationToken)
    {
        Guid[] tenantIds;
        await using (var discoveryScope = scopeFactory.CreateAsyncScope())
        {
            var discoveryDb = discoveryScope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantIds = await discoveryDb.Tenants.AsNoTracking().Select(x => x.Id).ToArrayAsync(cancellationToken);
        }

        foreach (var tenantId in tenantIds)
        {
            try
            {
                await ScheduleTenantSyncsAsync(tenantId, config, interval, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Scheduled sync dispatch failed for tenant {TenantId}.", tenantId);
            }
        }
    }

    private async Task ScheduleTenantSyncsAsync(
        Guid tenantId, MarketplaceSyncSchedulerOptions config, TimeSpan interval, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        using var _ = scope.ServiceProvider.GetRequiredService<ITenantScope>().BeginScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = timeProvider.GetUtcNow();
        var dueBefore = now.Subtract(interval);
        var connections = await db.MarketplaceConnections.AsNoTracking()
            .Where(x => x.Status == MarketplaceConnectionStates.Active &&
                        (x.LastSyncAt == null || x.LastSyncAt < dueBefore))
            .Select(x => new { x.Id, x.LastSyncAt })
            .ToArrayAsync(cancellationToken);
        if (connections.Length == 0)
            return;

        var gateway = scope.ServiceProvider.GetRequiredService<ConnectorGateway>();
        var queue = scope.ServiceProvider.GetRequiredService<MarketplaceSyncQueue>();
        var scheduledBucket = now.ToUnixTimeSeconds() / (long)interval.TotalSeconds;

        foreach (var connection in connections)
        {
            try
            {
                var leaseId = Guid.NewGuid();
                if (!await gateway.TryAcquireSyncLeaseAsync(connection.Id, leaseId, now, cancellationToken))
                    continue;
                var since = connection.LastSyncAt?.AddMinutes(-Math.Max(0, config.OverlapMinutes))
                    ?? now.AddHours(-Math.Max(1, config.InitialLookbackHours));
                var queued = false;
                try
                {
                    queued = await queue.EnqueueAsync(new MarketplaceSyncWorkItem(
                        tenantId, connection.Id, since, leaseId,
                        $"scheduled:{connection.Id:N}:{scheduledBucket}"), cancellationToken);
                    if (!queued)
                    {
                        var sync = scope.ServiceProvider.GetRequiredService<ConnectorSyncService>();
                        await sync.SyncAllAsync(connection.Id, since, cancellationToken);
                    }
                }
                finally
                {
                    if (!queued)
                        await gateway.ReleaseSyncLeaseAsync(connection.Id, leaseId, CancellationToken.None);
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Scheduled sync failed for connection {ConnectionId}.", connection.Id);
            }
        }
    }
}
