using Aggregator.Application.Sources;
using Microsoft.Extensions.Caching.Hybrid;

namespace Aggregator.Infrastructure.Sources.Caching;

/// <summary>Last-known-good items in <see cref="HybridCache"/> under their own <c>lkg:</c> keys, so they never pass for fresh answers.</summary>
internal sealed class HybridLastKnownGoodStore(HybridCache cache) : ILastKnownGoodStore
{
    private const string KeyPrefix = "lkg:";

    private static readonly HybridCacheEntryOptions ReadOnly = new()
    {
        Flags = HybridCacheEntryFlags.DisableUnderlyingData | HybridCacheEntryFlags.DisableLocalCacheWrite | HybridCacheEntryFlags.DisableDistributedCacheWrite,
    };

    public async ValueTask<LastKnownGood?> FindAsync(string key, CancellationToken cancellationToken)
    {
        var entry = await cache.GetOrCreateAsync<CachedItems?>(
            KeyPrefix + key, static _ => ValueTask.FromResult<CachedItems?>(null), ReadOnly, cancellationToken: cancellationToken);

        return entry is null ? null : new LastKnownGood(entry.Items, entry.StoredAt);
    }

    public ValueTask SaveAsync(string key, LastKnownGood entry, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return cache.SetAsync(
            KeyPrefix + key, new CachedItems(entry.Items, entry.StoredAt), HybridSourceCache.Lifetime(lifetime), cancellationToken: cancellationToken);
    }
}
