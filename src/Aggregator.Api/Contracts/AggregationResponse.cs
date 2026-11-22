using Aggregator.Application.Aggregation;

namespace Aggregator.Api.Contracts;

/// <summary>Body of a 200 from <c>GET /api/v1/aggregation</c>.</summary>
public sealed record AggregationResponse(
    IReadOnlyList<ItemResponse> Items,
    IReadOnlyList<SourceStatusResponse> Sources,
    PagingResponse Paging)
{
    public static AggregationResponse From(AggregationResult result, IReadOnlyList<SourceStatusResponse> sources)
    {
        ArgumentNullException.ThrowIfNull(result);
        var page = result.Items;
        return new AggregationResponse(
            [.. page.Items.Select(ItemResponse.From)],
            sources,
            new PagingResponse(page.Page, page.PageSize, page.TotalCount));
    }
}
