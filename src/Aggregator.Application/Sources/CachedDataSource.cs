using Aggregator.Application.Aggregation;

namespace Aggregator.Application.Sources;

/// <summary>
/// Serves a source's fresh cached answer, or fetches from the inner source once for concurrent callers and caches a live answer.
/// Failures and last-known-good fallbacks are returned but never cached. A caller that joined another caller's fetch reports it as cached.
/// </summary>
public sealed class CachedDataSource : IDataSource
{
    private readonly ISourceCache _cache;
    private readonly TimeSpan _ttl;
    private readonly TimeProvider _time;
    private readonly SourceCacheMetrics _metrics;

    public CachedDataSource(IDataSource inner, ISourceCache cache, TimeSpan ttl, TimeProvider time, SourceCacheMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(metrics);

        Inner = inner;
        _cache = cache;
        _ttl = ttl;
        _time = time;
        _metrics = metrics;
    }

    public IDataSource Inner { get; }

    public string Name => Inner.Name;

    public IReadOnlyList<string> Categories => Inner.Categories;

    public SearchFields UsedFields => Inner.UsedFields;

    public async Task<SourceResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var lookup = await _cache.GetOrFetchAsync(
            SourceCacheKey.For(Name, UsedFields, query), _ttl, token => Inner.FetchAsync(query, token), ToEntry, cancellationToken);

        switch (lookup)
        {
            case SourceCacheLookup.Hit { Entry: var entry }:
                _metrics.Hit(Name);
                return new SourceCached(entry.Items, Age(entry.StoredAt), entry.Provider);
            case SourceCacheLookup.Fetched { Result: SourceFallback { Provider: null } stale }:
                _metrics.Stale(Name);
                return stale;
            case SourceCacheLookup.Fetched { Result: var result }:
                _metrics.Miss(Name);
                return result;
            default:
                throw new InvalidOperationException($"Unknown cache lookup {lookup.GetType().Name}.");
        }
    }

    private CachedItems? ToEntry(SourceResult result) => result switch
    {
        SourceSuccess success => new CachedItems(success.Items, _time.GetUtcNow()),
        SourceFallback { Provider: { } provider } secondary => new CachedItems(secondary.Items, _time.GetUtcNow(), provider),
        _ => null,
    };

    private TimeSpan Age(DateTimeOffset storedAt)
    {
        var age = _time.GetUtcNow() - storedAt;
        return age > TimeSpan.Zero ? age : TimeSpan.Zero;
    }
}
