using Aggregator.Application.Sources;

namespace Aggregator.Application.UnitTests.TestData;

/// <summary>
/// A cache that keeps entries until their time to live passes on the given clock, and stores only what the entry mapping returns.
/// <see cref="AnswerForAnotherCaller"/> stands in for a concurrent caller whose fetch this call joined.
/// </summary>
internal sealed class FakeSourceCache(TimeProvider time) : ISourceCache
{
    private readonly Dictionary<string, (CachedItems Entry, DateTimeOffset ExpiresAt)> _entries = new(StringComparer.Ordinal);

    public CachedItems? AnswerForAnotherCaller { get; set; }

    public List<(string Key, TimeSpan Ttl)> Stored { get; } = [];

    public async ValueTask<SourceCacheLookup> GetOrFetchAsync(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<SourceResult>> fetch,
        Func<SourceResult, CachedItems?> toEntry,
        CancellationToken cancellationToken)
    {
        if (AnswerForAnotherCaller is { } joined)
        {
            return new SourceCacheLookup.Hit(joined);
        }

        if (_entries.TryGetValue(key, out var stored) && time.GetUtcNow() < stored.ExpiresAt)
        {
            return new SourceCacheLookup.Hit(stored.Entry);
        }

        var result = await fetch(cancellationToken);
        if (toEntry(result) is { } entry)
        {
            _entries[key] = (entry, time.GetUtcNow() + ttl);
            Stored.Add((key, ttl));
        }

        return new SourceCacheLookup.Fetched(result);
    }
}
