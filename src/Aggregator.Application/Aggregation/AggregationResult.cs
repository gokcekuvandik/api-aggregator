using Aggregator.Application.Paging;
using Aggregator.Application.Sources;
using Aggregator.Domain.Items;

namespace Aggregator.Application.Aggregation;

/// <summary>What one source did during an aggregation, and how long it took.</summary>
public sealed record SourceReport(string Name, SourceResult Result, TimeSpan Duration)
{
    /// <summary>True when the source returned live, cached or fallback data.</summary>
    public bool HasData => Result is not SourceFailure;
}

/// <summary>One source's report and its filtered, sorted items, streamed as soon as that source completes.</summary>
public sealed record SourceUpdate(SourceReport Report, IReadOnlyList<AggregatedItem> Items);

/// <summary>The paged, merged items plus a report per source.</summary>
public sealed record AggregationResult(PagedList<AggregatedItem> Items, IReadOnlyList<SourceReport> Sources)
{
    /// <summary>True when at least one source returned live, cached or fallback data.</summary>
    public bool HasData => Sources.Any(s => s.HasData);
}
