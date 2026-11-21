using Aggregator.Application.Sources;
using Aggregator.Infrastructure.Sources.News.HackerNews;
using Aggregator.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;

namespace Aggregator.Infrastructure.UnitTests.Sources.News;

public sealed class HackerNewsSourceTests : IDisposable
{
    private readonly SourceTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task FetchAsync_ShouldSearchStories_WhenQueryIsConfigured()
    {
        _host.Http.Expect($"{StubHttpClientFactory.DefaultBaseAddress}api/v1/search")
            .WithExactQueryString("tags=story&query=c%23%20%26%20f%23&hitsPerPage=5")
            .Respond("application/json", SourceTestHost.Fixture("hn-search.json"));

        await Source(new HackerNewsOptions { Query = "c# & f#", MaxItems = 5 })
            .FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        _host.Http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task FetchAsync_ShouldMapHits_WhenSearchReturnsStories()
    {
        _host.Http.When("*").Respond("application/json", SourceTestHost.Fixture("hn-search.json"));

        var result = await Source(new HackerNewsOptions()).FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        var items = result.ShouldBeOfType<SourceSuccess>().Items;
        items.Select(i => i.Id).ShouldBe(["news:4242", "news:4243"]);
        items[0].Url.ShouldBe(new Uri("https://example.com/aggregator"));
        items[0].Summary.ShouldBe("120 points by alice");
        items[0].PublishedAt.ShouldBe(new DateTimeOffset(2026, 11, 21, 8, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task FetchAsync_ShouldLinkToDiscussion_WhenHitHasNoUrl()
    {
        _host.Http.When("*").Respond("application/json", SourceTestHost.Fixture("hn-search.json"));

        var result = await Source(new HackerNewsOptions()).FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        result.Items.Single(i => i.Id == "news:4243").Url.ShouldBe(new Uri("https://news.ycombinator.com/item?id=4243"));
    }

    [Fact]
    public async Task FetchAsync_ShouldReturnEmptySuccess_WhenThereAreNoHits()
    {
        _host.Http.When("*").Respond("application/json", """{"hits":[]}""");

        var result = await Source(new HackerNewsOptions()).FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        result.ShouldBeOfType<SourceSuccess>().Items.ShouldBeEmpty();
    }

    [Fact]
    public void Source_ShouldDescribeItself_WhenListed()
    {
        var source = Source(new HackerNewsOptions());

        source.Name.ShouldBe("news");
        source.Categories.ShouldBe(["news"]);
        new HackerNewsOptions().BaseAddress.ShouldBe(new Uri("https://hn.algolia.com/"));
    }

    private HackerNewsSource Source(HackerNewsOptions options) => new(_host.Fetcher, Options.Create(options));
}
