using Aggregator.Application.Paging;
using Aggregator.Application.Sources;
using Aggregator.Domain.Items;

namespace Aggregator.Application.Aggregation;

/// <summary>What one source did during an aggregation, and how long it took.</summary>
public sealed record SourceReport(string Name, SourceResult Result, TimeSpan Duration);

/// <summary>The paged, merged items plus a report per source.</summary>
public sealed record AggregationResult(PagedList<AggregatedItem> Items, IReadOnlyList<SourceReport> Sources)
{
    /// <summary>
    /// True when at least one source produced a usable answer (live, cached or fallback).
    /// ADR-0002 maps true to 200 and false to 503.
    /// </summary>
    public bool HasData => Sources.Any(s => s.Result is not SourceFailure);
}
