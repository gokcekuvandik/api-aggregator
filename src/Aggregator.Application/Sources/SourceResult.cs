using Aggregator.Domain.Items;

namespace Aggregator.Application.Sources;

/// <summary>Outcome of fetching one source; only the four records below derive from it.</summary>
public abstract record SourceResult
{
    private protected SourceResult(IReadOnlyList<AggregatedItem> items) => Items = items;

    /// <summary>Items the source contributed; empty for a failure.</summary>
    public IReadOnlyList<AggregatedItem> Items { get; }
}
