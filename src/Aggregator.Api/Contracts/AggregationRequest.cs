using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Aggregator.Application.Aggregation;
using Aggregator.Application.Paging;
using Microsoft.AspNetCore.Mvc;

namespace Aggregator.Api.Contracts;

/// <summary>
/// Query string of <c>GET /api/v1/aggregation</c>.
/// Defaults sit on the constructor parameters so binding treats them as optional, and the attributes only describe the OpenAPI schema.
/// Blank text values count as missing; <c>sources</c> is a comma-separated list of source names.
/// </summary>
public sealed record AggregationRequest(
    [FromQuery(Name = "q")]
    [property: Display(Name = "q")]
    [property: StringLength(AggregationRequest.MaxTextLength)]
    string? Q = null,
    [FromQuery(Name = "city")]
    [property: Display(Name = "city")]
    [property: StringLength(AggregationRequest.MaxCityLength)]
    string? City = null,
    [FromQuery(Name = "sources")]
    [property: Display(Name = "sources")]
    [property: StringLength(AggregationRequest.MaxTextLength)]
    string? Sources = null,
    [FromQuery(Name = "category")]
    [property: Display(Name = "category")]
    [property: StringLength(AggregationRequest.MaxNameLength)]
    string? Category = null,
    [FromQuery(Name = "from")]
    [property: Display(Name = "from")]
    DateTimeOffset? From = null,
    [FromQuery(Name = "to")]
    [property: Display(Name = "to")]
    DateTimeOffset? To = null,
    [FromQuery(Name = "sortBy")]
    [property: Display(Name = "sortBy")]
    [property: Description("One of: " + SortParameters.FieldNames + ". Defaults to relevance.")]
    [property: StringLength(AggregationRequest.MaxNameLength)]
    string? SortBy = null,
    [FromQuery(Name = "sortOrder")]
    [property: Display(Name = "sortOrder")]
    [property: Description("One of: " + SortParameters.DirectionNames + ". Defaults to desc.")]
    [property: StringLength(AggregationRequest.MaxNameLength)]
    string? SortOrder = null,
    [FromQuery(Name = "page")]
    [property: Display(Name = "page")]
    [property: Range(1, int.MaxValue)]
    int Page = 1,
    [FromQuery(Name = "pageSize")]
    [property: Display(Name = "pageSize")]
    [property: Range(1, PageRequest.MaxPageSize)]
    int PageSize = PageRequest.DefaultPageSize)
{
    public const int MaxTextLength = 200;
    public const int MaxCityLength = 100;
    public const int MaxNameLength = 50;

    /// <summary>Call only on a request that passed <see cref="AggregationRequestValidator"/>.</summary>
    public AggregationQuery ToQuery() => new(Page, PageSize)
    {
        Text = Q,
        City = City,
        Sources = SplitNames(Sources),
        Category = Category,
        From = From,
        To = To,
        SortBy = SortParameters.ParseField(SortBy),
        SortOrder = SortParameters.ParseDirection(SortOrder),
    };

    private static string[] SplitNames(string? value) =>
        value is null
            ? []
            : [.. value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase)];
}
