using Aggregator.Application.Paging;
using Aggregator.Domain.Items;

namespace Aggregator.Application.Aggregation;

/// <summary>The paged, merged items plus a report per source.</summary>
public sealed record AggregationResult(PagedList<AggregatedItem> Items, IReadOnlyList<SourceReport> Sources)
{
    /// <summary>True when at least one source returned live, cached or fallback data.</summary>
    public bool HasData => Sources.Any(s => s.HasData);
}
