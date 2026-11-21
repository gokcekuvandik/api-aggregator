using System.ComponentModel.DataAnnotations;
using Aggregator.Application.Aggregation;
using Aggregator.Application.Paging;
using Microsoft.AspNetCore.Mvc;

namespace Aggregator.Api.Contracts;

/// <summary>
/// Query string of <c>GET /api/v1/aggregation</c>.
/// Defaults sit on the constructor parameters so binding treats them as optional.
/// </summary>
public sealed record AggregationRequest(
    [FromQuery(Name = "page")]
    [property: Display(Name = "page")]
    [property: Range(1, int.MaxValue)]
    int Page = 1,
    [FromQuery(Name = "pageSize")]
    [property: Display(Name = "pageSize")]
    [property: Range(1, PageRequest.MaxPageSize)]
    int PageSize = PageRequest.DefaultPageSize)
{
    public AggregationQuery ToQuery() => new(Page, PageSize);
}
