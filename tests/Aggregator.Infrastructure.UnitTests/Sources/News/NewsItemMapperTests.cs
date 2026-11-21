using Aggregator.Domain.Items;
using Aggregator.Infrastructure.Sources.News;
using Aggregator.Infrastructure.Sources.Relevance;
using Aggregator.Infrastructure.UnitTests.TestDoubles;

namespace Aggregator.Infrastructure.UnitTests.Sources.News;

public sealed class NewsItemMapperTests
{
    private static readonly DateTimeOffset Morning = new(2026, 11, 21, 8, 30, 0, TimeSpan.Zero);
    private static readonly RankRelevanceScorer Scorer = new();

    [Fact]
    public void ToItems_ShouldCopyArticle_WhenArticleIsComplete()
    {
        var article = new NewsArticle("42", "Title", "Summary", new Uri("https://example.com/a"), Morning, Author: "alice", Points: 3, Publisher: "Example");

        var item = ToItems(article).ShouldHaveSingleItem();

        item.ShouldBe(new AggregatedItem(
            "news:42", "news", "news", "Title", "Summary", new Uri("https://example.com/a"), Morning,
            RelevanceScore.From(1), new NewsDetails(Author: "alice", Points: 3, Publisher: "Example")));
    }

    [Fact]
    public void ToItems_ShouldScoreByRank_WhenBlankTitlesAreSkipped()
    {
        var items = ToItems(Article("first"), Article(" "), Article("second"));

        items.Select(item => (item.Title, item.Relevance.Value)).ShouldBe([("first", 1.0), ("second", 0.5)]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ToItems_ShouldSkipArticle_WhenTitleIsBlank(string title) =>
        ToItems(Article(title)).ShouldBeEmpty();

    [Fact]
    public void ToItems_ShouldTrimTitle_WhenTitleHasSurroundingWhitespace() =>
        ToItems(Article("  Title \n")).ShouldHaveSingleItem().Title.ShouldBe("Title");

    private static NewsArticle Article(string title) => new("1", title, null, null, Morning);

    private static IReadOnlyList<AggregatedItem> ToItems(params NewsArticle[] articles) =>
        NewsItemMapper.ToItems("news", articles, Scorer, SourceTestHost.Query);
}
