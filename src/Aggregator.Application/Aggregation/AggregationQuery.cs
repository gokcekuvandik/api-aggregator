using System.ComponentModel.DataAnnotations;
using Aggregator.Application.Paging;

namespace Aggregator.Application.Aggregation;

/// <summary>
/// Query for <c>GET /api/v1/aggregation</c>, bound with <c>[AsParameters]</c> and checked by
/// the built-in validation filter. ADR-0005 adds the filter and sort parameters.
/// </summary>
public sealed record AggregationQuery(
    [property: Range(1, int.MaxValue)] int Page = 1,
    [property: Range(1, PageRequest.MaxPageSize)] int PageSize = PageRequest.DefaultPageSize);
