using Aggregator.Domain.Items;

namespace Aggregator.Application.Sources;

/// <summary>Items a source answered live, when they were stored, and the secondary provider that answered, if any.</summary>
public sealed record CachedItems(IReadOnlyList<AggregatedItem> Items, DateTimeOffset StoredAt, string? Provider = null);

/// <summary>What a cache lookup gave: a stored entry, or the result this lookup fetched or joined.</summary>
public abstract record SourceCacheLookup
{
    private SourceCacheLookup()
    {
    }

    /// <summary>A stored entry, or one stored by a concurrent caller's fetch that this lookup joined.</summary>
    public sealed record Hit(CachedItems Entry) : SourceCacheLookup;

    /// <summary>The result of this lookup's fetch, or of a joined fetch whose result was not stored.</summary>
    public sealed record Fetched(SourceResult Result) : SourceCacheLookup;
}

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
