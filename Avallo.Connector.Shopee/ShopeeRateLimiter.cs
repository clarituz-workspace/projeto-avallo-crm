using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Avallo.Connectors.Abstractions;
using StackExchange.Redis;

namespace Avallo.Connector.Shopee;

public sealed class ShopeeRateLimiter(IConnectionMultiplexer? redis = null) : IDisposable
{
    private readonly IConnectionMultiplexer? _redis = redis;
    private readonly ConcurrentDictionary<long, TokenBucketRateLimiter> _shops = new();

    public async ValueTask AcquireAsync(long shopId, CancellationToken cancellationToken)
    {
        if (_redis is not null)
        {
            await AcquireDistributedAsync($"ratelimit:shopee:{shopId}", 1000, cancellationToken);
            return;
        }
        var limiter = _shops.GetOrAdd(shopId, _ => new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 1000,
            TokensPerPeriod = 1000,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 1000,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        }));
        using var lease = await limiter.AcquireAsync(1, cancellationToken);
        if (!lease.IsAcquired)
            throw new ConnectorException("shopee", "rate_limit_queue_full", "Shopee request queue is full.", true);
    }

    private async Task AcquireDistributedAsync(string key, int limit, CancellationToken cancellationToken)
    {
        const string script = "local n=redis.call('INCR',KEYS[1]); if n == 1 then redis.call('EXPIRE',KEYS[1],60) end; return n";
        while (true)
        {
            var count = (long)await _redis!.GetDatabase().ScriptEvaluateAsync(script, [new RedisKey(key)]);
            if (count <= limit) return;
            await Task.Delay(250, cancellationToken);
        }
    }

    public void Dispose()
    {
        foreach (var limiter in _shops.Values) limiter.Dispose();
        _shops.Clear();
    }
}
