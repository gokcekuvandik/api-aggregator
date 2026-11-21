using Aggregator.Domain.Items;
using Aggregator.Infrastructure.Sources.News;

namespace Aggregator.Infrastructure.UnitTests.Sources.News;

public sealed class NewsItemMapperTests
{
    private static readonly DateTimeOffset Morning = new(2026, 11, 21, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void ToItems_ShouldCopyArticle_WhenArticleIsComplete()
    {
        var article = new NewsArticle("42", "Title", "Summary", new Uri("https://example.com/a"), Morning);

        var item = NewsItemMapper.ToItems("news", [article]).ShouldHaveSingleItem();

        item.ShouldBe(new AggregatedItem("news:42", "news", "news", "Title", "Summary", new Uri("https://example.com/a"), Morning));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ToItems_ShouldSkipArticle_WhenTitleIsBlank(string title) =>
        NewsItemMapper.ToItems("news", [new NewsArticle("1", title, null, null, Morning)]).ShouldBeEmpty();

    [Fact]
    public void ToItems_ShouldTrimTitle_WhenTitleHasSurroundingWhitespace() =>
        NewsItemMapper.ToItems("news", [new NewsArticle("1", "  Title \n", null, null, Morning)])
            .ShouldHaveSingleItem().Title.ShouldBe("Title");
}
