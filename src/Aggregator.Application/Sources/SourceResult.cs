using Aggregator.Domain.Items;

namespace Aggregator.Application.Sources;

/// <summary>
/// Outcome of fetching one source (ADR-0006). The hierarchy is closed: only the four
/// records below derive from it, so callers can switch over it exhaustively.
/// </summary>
public abstract record SourceResult
{
    private protected SourceResult(IReadOnlyList<AggregatedItem> items) => Items = items;

    /// <summary>Items the source contributed; empty for a failure.</summary>
    public IReadOnlyList<AggregatedItem> Items { get; }
}

/// <summary>The source answered live.</summary>
public sealed record SourceSuccess(IReadOnlyList<AggregatedItem> Items) : SourceResult(Items);

/// <summary>The answer came from a fresh cache entry.</summary>
public sealed record SourceCached(IReadOnlyList<AggregatedItem> Items) : SourceResult(Items);

/// <summary>The source failed; these are last-known-good items of the given age.</summary>
public sealed record SourceFallback(IReadOnlyList<AggregatedItem> Items, TimeSpan Age) : SourceResult(Items);

/// <summary>The source failed and nothing could stand in for it.</summary>
public sealed record SourceFailure(string Error) : SourceResult([]);
