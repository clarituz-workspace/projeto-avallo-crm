using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Avallo.Web.Infrastructure;

namespace Avallo.Web.Features.Notifications;

public sealed class NotificationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationWorkerOptions> options,
    IOptions<AzureCommunicationEmailOptions> emailOptions,
    ILogger<NotificationWorker> logger) : BackgroundService
{
    private readonly NotificationWorkerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_options.Enabled || !emailOptions.Value.Enabled)
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
                return;
            }
            try
            {
                await ProcessAllTenantsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Notification worker cycle failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(_options.IntervalMinutes), stoppingToken);
        }
    }

    private async Task ProcessAllTenantsAsync(CancellationToken cancellationToken)
    {
        Guid[] tenantIds;
        await using (var discoveryScope = scopeFactory.CreateAsyncScope())
        {
            var db = discoveryScope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantIds = await db.Tenants.AsNoTracking().Select(x => x.Id).ToArrayAsync(cancellationToken);
        }

        foreach (var tenantId in tenantIds)
        {
            try
            {
                await ProcessTenantAsync(tenantId, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Notification processing failed for tenant {TenantId}.", tenantId);
            }
        }
    }

    private async Task ProcessTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantScope>();
        using var _ = tenantScope.BeginScope(tenantId);
        await scope.ServiceProvider.GetRequiredService<NotificationScheduler>().RunAsync(cancellationToken);

        var sender = scope.ServiceProvider.GetRequiredService<AzureCommunicationEmailSender>();
        if (!sender.IsEnabled)
            return;

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var emails = await db.EmailOutbox
            .Where(x => x.SentAt == null && x.NextAttemptAt <= now && x.AttemptCount < 10 &&
                        (x.LeaseUntil == null || x.LeaseUntil < now))
            .OrderBy(x => x.CreatedAt)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);
        foreach (var email in emails)
        {
            var leaseId = Guid.NewGuid();
            var leaseUntil = now.AddMinutes(30);
            var claimed = await db.EmailOutbox
                .Where(x => x.Id == email.Id && x.SentAt == null && x.AttemptCount < 10 &&
                            (x.LeaseUntil == null || x.LeaseUntil < now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.LeaseId, leaseId)
                    .SetProperty(x => x.LeaseUntil, leaseUntil), cancellationToken);
            if (claimed == 0)
                continue;
            email.LeaseId = leaseId;
            email.LeaseUntil = leaseUntil;
            try
            {
                await sender.SendAsync(email, cancellationToken);
                email.SentAt = now;
                email.LastError = null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                email.AttemptCount++;
                email.LastError = exception.Message;
                email.NextAttemptAt = now.AddMinutes(Math.Min(Math.Pow(2, email.AttemptCount), 360));
                logger.LogWarning(exception, "Email {EmailId} delivery failed on attempt {Attempt}.", email.Id, email.AttemptCount);
            }
            email.LeaseId = null;
            email.LeaseUntil = null;
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
