using System.ComponentModel.DataAnnotations;
using Aggregator.Application.Aggregation;
using Aggregator.Application.Paging;
using Microsoft.AspNetCore.Mvc;

namespace Aggregator.Api.Contracts;

/// <summary>
/// Query string of <c>GET /api/v1/aggregation</c>, bound with <c>[AsParameters]</c>.
/// The explicit names keep the wire contract camelCase in binding, OpenAPI and validation errors.
/// Defaults sit on the constructor parameters: <c>[AsParameters]</c> treats them as optional,
/// while a property initialiser would leave the parameter required. ADR-0005 adds filters and sorting.
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
