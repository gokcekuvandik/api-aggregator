namespace Aggregator.Application.Sources;

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
