using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Aggregator.Infrastructure.Sources.Caching;

/// <summary>
/// Stops calling the second level for <see cref="BreakDuration"/> after it fails, so a hung Redis costs one timeout instead of one per request.
/// While open, reads miss and writes are skipped, and HybridCache answers from memory.
/// After the break one call probes Redis while the others keep skipping, and a success closes it; a caller's own cancellation never opens it.
/// </summary>
internal sealed partial class CircuitBreakingDistributedCache(IDistributedCache inner, TimeProvider time, ILogger<CircuitBreakingDistributedCache> logger) : IDistributedCache, IDisposable
{
    public static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(30);

    /// <summary>How long the other callers keep skipping while one call probes Redis.</summary>
    public static readonly TimeSpan ProbeWindow = TimeSpan.FromSeconds(5);

    private const long Closed = long.MinValue;

    private long openUntilTicks = Closed;

    // The container only sees the wrapper, so the wrapper disposes the Redis connection.
    public void Dispose() => (inner as IDisposable)?.Dispose();

    public byte[]? Get(string key) => IsOpen() ? null : Guard(() => inner.Get(key));

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
        IsOpen() ? Task.FromResult<byte[]?>(null) : GuardAsync(() => inner.GetAsync(key, token), token);

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        if (!IsOpen())
        {
            Guard(() => inner.Set(key, value, options));
        }
    }

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
        IsOpen() ? Task.CompletedTask : GuardAsync(() => inner.SetAsync(key, value, options, token), token);

    public void Refresh(string key)
    {
        if (!IsOpen())
        {
            Guard(() => inner.Refresh(key));
        }
    }

    public Task RefreshAsync(string key, CancellationToken token = default) =>
        IsOpen() ? Task.CompletedTask : GuardAsync(() => inner.RefreshAsync(key, token), token);

    public void Remove(string key)
    {
        if (!IsOpen())
        {
            Guard(() => inner.Remove(key));
        }
    }

    public Task RemoveAsync(string key, CancellationToken token = default) =>
        IsOpen() ? Task.CompletedTask : GuardAsync(() => inner.RemoveAsync(key, token), token);

    private bool IsOpen()
    {
        var until = Interlocked.Read(ref openUntilTicks);
        if (until == Closed)
        {
            return false;
        }

        // Once the break has passed, only the caller that moves the deadline gets through as the probe.
        var now = time.GetUtcNow().UtcTicks;
        return now < until || Interlocked.CompareExchange(ref openUntilTicks, now + ProbeWindow.Ticks, until) != until;
    }

    private void Close()
    {
        if (Interlocked.Read(ref openUntilTicks) != Closed)
        {
            Interlocked.Exchange(ref openUntilTicks, Closed);
        }
    }

    private T Guard<T>(Func<T> call)
    {
        try
        {
            var result = call();
            Close();
            return result;
        }
        catch (Exception exception)
        {
            Open(exception);
            throw;
        }
    }

    private void Guard(Action call) => Guard(() =>
    {
        call();
        return true;
    });

    private async Task<T> GuardAsync<T>(Func<Task<T>> call, CancellationToken token)
    {
        try
        {
            var result = await call().ConfigureAwait(false);
            Close();
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Open(exception);
            throw;
        }
    }

    private Task<bool> GuardAsync(Func<Task> call, CancellationToken token) => GuardAsync(async () =>
    {
        await call().ConfigureAwait(false);
        return true;
    }, token);

    private void Open(Exception exception)
    {
        var previous = Interlocked.Exchange(ref openUntilTicks, (time.GetUtcNow() + BreakDuration).UtcTicks);
        if (previous == Closed)
        {
            LogOpened(exception, BreakDuration.TotalSeconds);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Redis second level failed; it is skipped for {BreakSeconds} s and the cache answers from memory")]
    private partial void LogOpened(Exception exception, double breakSeconds);
}
