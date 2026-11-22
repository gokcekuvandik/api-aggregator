namespace Aggregator.Api.Contracts;

/// <summary>One item; <c>relevance</c> is a 0–1 heuristic normalised per source, rounded to four decimals.</summary>
public sealed record ItemResponse(
    string Id,
    string Source,
    string Category,
    string Title,
    string? Summary,
    Uri? Url,
    DateTimeOffset PublishedAt,
    double Relevance,
    ItemDetailsResponse Details)
{
    private const int RelevanceDecimals = 4;

    public static ItemResponse From(Domain.Items.AggregatedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new(
            item.Id,
            item.Source,
            item.Category,
            item.Title,
            item.Summary,
            item.Url,
            item.PublishedAt,
            Math.Round(item.Relevance.Value, RelevanceDecimals),
            ItemDetailsResponse.From(item.Details));
    }
}
