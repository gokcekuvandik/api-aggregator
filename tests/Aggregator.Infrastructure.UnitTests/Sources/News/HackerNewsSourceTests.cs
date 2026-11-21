using Aggregator.Application.Sources;
using Aggregator.Domain.Items;
using Aggregator.Infrastructure.Sources.News.HackerNews;
using Aggregator.Infrastructure.Sources.Relevance;
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
    public async Task FetchAsync_ShouldSendOnConfiguredClient_WhenRegisteredAsSecondary()
    {
        _host.Http.When("*").Respond("application/json", SourceTestHost.Fixture("hn-search.json"));

        await Source(new HackerNewsOptions { ClientName = "news-secondary" }).FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        _host.Factory.RequestedNames.ShouldBe(["news-secondary"]);
    }

    [Fact]
    public async Task FetchAsync_ShouldSearchForRequestText_WhenRequestHasText()
    {
        _host.Http.Expect("*").WithExactQueryString("tags=story&query=rust%20lang&hitsPerPage=20").Respond("application/json", """{"hits":[]}""");

        await Source(new HackerNewsOptions()).FetchAsync(SourceTestHost.Query with { Text = "rust lang" }, TestContext.Current.CancellationToken);

        _host.Http.VerifyNoOutstandingExpectation();
    }

    public static TheoryData<string, DateTimeOffset?, DateTimeOffset?, string> DateRanges => new()
    {
        { "both bounds", Day20, Day21, "&numericFilters=created_at_i%3E%3D1795132800%2Ccreated_at_i%3C%3D1795219200" },
        { "from only", Day20, null, "&numericFilters=created_at_i%3E%3D1795132800" },
        { "to only, rounded up to the second", null, Day21.AddMilliseconds(1), "&numericFilters=created_at_i%3C%3D1795219201" },
        { "to at the latest representable time, clamped instead of overflowing", null, DateTimeOffset.MaxValue, "&numericFilters=created_at_i%3C%3D253402300799" },
    };

    [Theory]
    [MemberData(nameof(DateRanges))]
    public async Task FetchAsync_ShouldFilterByCreationTime_WhenRequestHasDateRange(string scenario, DateTimeOffset? from, DateTimeOffset? to, string filter)
    {
        _host.Http.Expect("*").WithExactQueryString($"tags=story&query=dotnet&hitsPerPage=20{filter}").Respond("application/json", """{"hits":[]}""");

        await Source(new HackerNewsOptions()).FetchAsync(SourceTestHost.Query with { From = from, To = to }, TestContext.Current.CancellationToken);

        _host.Http.VerifyNoOutstandingExpectation();
        scenario.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task FetchAsync_ShouldMapDetailsAndRank_WhenSearchReturnsStories()
    {
        _host.Http.When("*").Respond("application/json", SourceTestHost.Fixture("hn-search.json"));

        var result = await Source(new HackerNewsOptions()).FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        var items = result.ShouldBeOfType<SourceSuccess>().Items;
        items[0].Details.ShouldBe(new NewsDetails(Author: "alice", Points: 120, Publisher: null));
        items.Select(i => i.Relevance.Value).ShouldBe([1.0, 0.5]);
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

    private static readonly DateTimeOffset Day20 = new(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day21 = new(2026, 11, 21, 0, 0, 0, TimeSpan.Zero);

    private HackerNewsSource Source(HackerNewsOptions options) => new(_host.Fetcher, Options.Create(options), new RankRelevanceScorer());
}
