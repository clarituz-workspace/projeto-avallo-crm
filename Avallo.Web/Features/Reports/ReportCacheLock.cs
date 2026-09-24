using System.Collections.Concurrent;
using StackExchange.Redis;

namespace Avallo.Web.Features.Reports;

internal sealed class ReportCacheLock(IServiceProvider services)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LocalLocks = new();
    private const string ReleaseScript = "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    public async Task<IAsyncDisposable> AcquireAsync(string key, TimeSpan expiry, CancellationToken cancellationToken)
    {
        var multiplexer = services.GetService<IConnectionMultiplexer>();
        if (multiplexer is null)
        {
            var localLock = LocalLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await localLock.WaitAsync(cancellationToken);
            return new LocalLease(localLock);
        }

        var token = Guid.NewGuid().ToString("N");
        var redisKey = $"report-lock:{key}";
        var database = multiplexer.GetDatabase();
        while (true)
        {
            if (await database.StringSetAsync(redisKey, token, expiry, When.NotExists))
                return new RedisLease(database, redisKey, token);

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }

    private sealed class LocalLease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RedisLease(IDatabase database, string key, string token) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() =>
            await database.ScriptEvaluateAsync(ReleaseScript, [key], [token]);
    }
}
