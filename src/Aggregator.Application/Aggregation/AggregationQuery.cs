using Aggregator.Application.Paging;

namespace Aggregator.Application.Aggregation;

/// <summary>Paging input for an aggregation.</summary>
public sealed record AggregationQuery(int Page, int PageSize);
