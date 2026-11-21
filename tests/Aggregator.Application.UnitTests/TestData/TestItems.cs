using Aggregator.Domain.Items;

namespace Aggregator.Application.UnitTests.TestData;

/// <summary>Builds <see cref="AggregatedItem"/> test data with ids <c>{source}-1</c>, <c>{source}-2</c>, …</summary>
internal static class TestItems
{
    public static readonly DateTimeOffset PublishedAt = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<AggregatedItem> Many(int count, string source = "news") =>
        [.. Enumerable.Range(1, count).Select(i => Item($"{source}-{i}", source))];

    public static AggregatedItem Item(
        string id,
        string source = "news",
        string category = "news",
        DateTimeOffset? publishedAt = null,
        double relevance = 0.5) => new(
            Id: id,
            Source: source,
            Category: category,
            Title: $"Title of {id}",
            Summary: null,
            Url: null,
            PublishedAt: publishedAt ?? PublishedAt,
            Relevance: RelevanceScore.From(relevance),
            Details: new NewsDetails(Author: null, Points: null, Publisher: null));
}
