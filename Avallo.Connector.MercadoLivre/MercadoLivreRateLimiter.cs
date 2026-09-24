using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using StackExchange.Redis;

namespace Avallo.Connector.MercadoLivre;

public sealed class MercadoLivreRateLimiter(IConnectionMultiplexer? redis = null) : IDisposable
{
    private readonly IConnectionMultiplexer? _redis = redis;
    private readonly ConcurrentDictionary<string, TokenBucketRateLimiter> _limiters = new();

    public async ValueTask AcquireAsync(string accessToken, CancellationToken cancellationToken)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)));
        if (_redis is not null)
        {
            await AcquireDistributedAsync($"ratelimit:mercadolivre:{key}", 100, cancellationToken);
            return;
        }
        var limiter = _limiters.GetOrAdd(key, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 100,
            TokensPerPeriod = 100,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 500,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        }.CreateLimiter());
        using var lease = await limiter.AcquireAsync(1, cancellationToken);
        if (!lease.IsAcquired)
            throw new TimeoutException("Mercado Livre request rate limit queue is full.");
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
        foreach (var limiter in _limiters.Values) limiter.Dispose();
        _limiters.Clear();
    }
}

internal static class RateLimiterOptionsExtensions
{
    public static TokenBucketRateLimiter CreateLimiter(this TokenBucketRateLimiterOptions options) => new(options);
}
