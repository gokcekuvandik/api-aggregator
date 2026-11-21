using Aggregator.Domain.Items;

namespace Aggregator.Application.UnitTests.TestData;

/// <summary>Builds <see cref="AggregatedItem"/> instances with sensible defaults.</summary>
internal sealed class ItemBuilder
{
    private string _id = "item-1";
    private string _source = "news";

    public ItemBuilder WithId(string id)
    {
        _id = id;
        return this;
    }

    public ItemBuilder FromSource(string source)
    {
        _source = source;
        return this;
    }

    public AggregatedItem Build() => new(
        Id: _id,
        Source: _source,
        Category: "news",
        Title: $"Title of {_id}",
        Summary: null,
        Url: null,
        PublishedAt: new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero));

    public static IReadOnlyList<AggregatedItem> Many(int count, string source = "news") =>
        [.. Enumerable.Range(1, count).Select(i => new ItemBuilder().WithId($"{source}-{i}").FromSource(source).Build())];
}
