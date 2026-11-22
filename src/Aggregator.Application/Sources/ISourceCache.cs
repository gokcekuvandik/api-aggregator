namespace Aggregator.Application.Sources;

/// <summary>Fresh source answers by cache key, shared by concurrent callers.</summary>
public interface ISourceCache
{
    /// <summary>
    /// Returns the entry for <paramref name="key"/> if it is younger than <paramref name="ttl"/>; otherwise runs <paramref name="fetch"/>
    /// once for all concurrent callers of the key and stores what <paramref name="toEntry"/> makes of its result.
    /// Nothing is stored when <paramref name="toEntry"/> returns null or <paramref name="fetch"/> throws.
    /// </summary>
    ValueTask<SourceCacheLookup> GetOrFetchAsync(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<SourceResult>> fetch,
        Func<SourceResult, CachedItems?> toEntry,
        CancellationToken cancellationToken);
}
