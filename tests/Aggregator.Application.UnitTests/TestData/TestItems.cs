using Aggregator.Domain.Items;

namespace Aggregator.Application.UnitTests.TestData;

/// <summary>Builds <see cref="AggregatedItem"/> test data with ids <c>{source}-1</c>, <c>{source}-2</c>, …</summary>
internal static class TestItems
{
    private static readonly DateTimeOffset PublishedAt = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<AggregatedItem> Many(int count, string source = "news") =>
        [.. Enumerable.Range(1, count).Select(i => new AggregatedItem(
            Id: $"{source}-{i}",
            Source: source,
            Category: "news",
            Title: $"Title of {source}-{i}",
            Summary: null,
            Url: null,
            PublishedAt: PublishedAt))];
}
