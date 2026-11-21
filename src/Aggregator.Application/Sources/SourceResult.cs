using Aggregator.Domain.Items;

namespace Aggregator.Application.Sources;

/// <summary>Outcome of fetching one source; only the four records below derive from it.</summary>
public abstract record SourceResult
{
    private protected SourceResult(IReadOnlyList<AggregatedItem> items) => Items = items;

    /// <summary>Items the source contributed; empty for a failure.</summary>
    public IReadOnlyList<AggregatedItem> Items { get; }
}

/// <summary>The source answered live.</summary>
public sealed record SourceSuccess(IReadOnlyList<AggregatedItem> Items) : SourceResult(Items);

/// <summary>The answer came from a fresh cache entry stored <paramref name="Age"/> ago, live from the primary or from the named secondary provider.</summary>
public sealed record SourceCached(IReadOnlyList<AggregatedItem> Items, TimeSpan Age, string? Provider = null) : SourceResult(Items);

/// <summary>
/// The primary provider failed. The items come from the named secondary provider (age zero),
/// or, when <paramref name="Provider"/> is null, from the last-known-good store at the given age.
/// </summary>
public sealed record SourceFallback(IReadOnlyList<AggregatedItem> Items, TimeSpan Age, string? Provider = null) : SourceResult(Items);

/// <summary>The source failed and nothing could stand in for it.</summary>
public sealed record SourceFailure(string Error) : SourceResult([])
{
    public static SourceFailure TimedOut(string sourceName) => new($"{sourceName} timed out.");
}
