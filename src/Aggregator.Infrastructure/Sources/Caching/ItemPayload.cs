using Aggregator.Domain.Items;

namespace Aggregator.Infrastructure.Sources.Caching;

internal sealed record ItemPayload(
    string Id,
    string Source,
    string Category,
    string Title,
    string? Summary,
    Uri? Url,
    DateTimeOffset PublishedAt,
    double Relevance,
    DetailsPayload Details)
{
    public static ItemPayload From(AggregatedItem item) => new(
        item.Id, item.Source, item.Category, item.Title, item.Summary, item.Url, item.PublishedAt, item.Relevance.Value, DetailsPayload.From(item.Details));

    public AggregatedItem ToItem() => new(Id, Source, Category, Title, Summary, Url, PublishedAt, RelevanceScore.From(Relevance), Details.ToDetails());
}
