using Aggregator.Application.Paging;

namespace Aggregator.Application.Aggregation;

/// <summary>
/// What to aggregate. HTTP binding, defaults and validation live in the Api's <c>AggregationRequest</c>;
/// <see cref="PageRequest.Apply{T}"/> still guards the paging range. ADR-0005 adds filters and sorting.
/// </summary>
public sealed record AggregationQuery(int Page, int PageSize);
