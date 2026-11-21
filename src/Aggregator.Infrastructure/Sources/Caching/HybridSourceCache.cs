using Aggregator.Application.Sources;
using Microsoft.Extensions.Caching.Hybrid;

namespace Aggregator.Infrastructure.Sources.Caching;

/// <summary>Fresh source answers in <see cref="HybridCache"/>, which runs one fetch per key for concurrent callers.</summary>
internal sealed class HybridSourceCache(HybridCache cache) : ISourceCache
{
    public async ValueTask<SourceCacheLookup> GetOrFetchAsync(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<SourceResult>> fetch,
        Func<SourceResult, CachedItems?> toEntry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        ArgumentNullException.ThrowIfNull(toEntry);

        SourceResult? fetched = null;
        try
        {
            var entry = await cache.GetOrCreateAsync(
                key,
                async token =>
                {
                    fetched = await fetch(token);
                    return toEntry(fetched) ?? throw new UnstoredResultException(fetched);
                },
                Lifetime(ttl),
                cancellationToken: cancellationToken);

            return fetched is null ? new SourceCacheLookup.Hit(entry) : new SourceCacheLookup.Fetched(fetched);
        }
        catch (UnstoredResultException unstored)
        {
            return new SourceCacheLookup.Fetched(unstored.Result);
        }
    }

    internal static HybridCacheEntryOptions Lifetime(TimeSpan ttl) => new() { Expiration = ttl, LocalCacheExpiration = ttl };

    /// <summary>HybridCache stores whatever its factory returns, so a result that must not be stored leaves the factory as this exception, which reaches every joined caller.</summary>
    private sealed class UnstoredResultException(SourceResult result) : Exception("The source result is not stored.")
    {
        public SourceResult Result { get; } = result;
    }
}
