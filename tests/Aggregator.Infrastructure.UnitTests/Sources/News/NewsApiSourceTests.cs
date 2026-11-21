using Aggregator.Application.Sources;
using Aggregator.Domain.Items;
using Aggregator.Infrastructure.Sources.News.NewsApi;
using Aggregator.Infrastructure.Sources.Relevance;
using Aggregator.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;

namespace Aggregator.Infrastructure.UnitTests.Sources.News;

public sealed class NewsApiSourceTests : IDisposable
{
    private const string ApiKey = "newsapi-test-key";

    private readonly SourceTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task FetchAsync_ShouldSendKeyInHeader_WhenKeyIsConfigured()
    {
        _host.Http.Expect($"{StubHttpClientFactory.DefaultBaseAddress}v2/everything")
            .WithExactQueryString("q=dotnet&pageSize=20&sortBy=publishedAt&language=en")
            .WithHeaders("X-Api-Key", ApiKey)
            .Respond("application/json", SourceTestHost.Fixture("newsapi-everything.json"));

        await Source().FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        _host.Http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task FetchAsync_ShouldSortByRelevancy_WhenRequestHasText()
    {
        _host.Http.Expect("*").WithExactQueryString("q=rust%20lang&pageSize=20&sortBy=relevancy&language=en").Respond("application/json", """{"articles":[]}""");

        await Source().FetchAsync(SourceTestHost.Query with { Text = "rust lang" }, TestContext.Current.CancellationToken);

        _host.Http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task FetchAsync_ShouldPassDateRange_WhenRequestHasOne()
    {
        _host.Http.Expect("*")
            .WithExactQueryString("q=dotnet&pageSize=20&sortBy=publishedAt&language=en&from=2026-11-20T10%3A00%3A00Z&to=2026-11-21T00%3A00%3A01Z")
            .Respond("application/json", """{"articles":[]}""");
        var query = SourceTestHost.Query with
        {
            From = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.FromHours(2)),
            To = new DateTimeOffset(2026, 11, 21, 0, 0, 0, 500, TimeSpan.Zero),
        };

        await Source().FetchAsync(query, TestContext.Current.CancellationToken);

        _host.Http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task FetchAsync_ShouldMapDetailsAndRank_WhenArticlesArrive()
    {
        _host.Http.When("*").Respond("application/json", SourceTestHost.Fixture("newsapi-everything.json"));

        var result = await Source().FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        var items = result.ShouldBeOfType<SourceSuccess>().Items;
        items[0].Details.ShouldBe(new NewsDetails(Author: "Dana", Points: null, Publisher: "Example Tech"));
        items.Select(i => i.Relevance.Value).ShouldBe([1.0, 0.5]);
    }

    [Fact]
    public async Task FetchAsync_ShouldDropRemovedArticles_WhenNewsApiRedactsThem()
    {
        _host.Http.When("*").Respond("application/json", SourceTestHost.Fixture("newsapi-everything.json"));

        var result = await Source().FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        result.ShouldBeOfType<SourceSuccess>().Items.Select(i => i.Title).ShouldBe([".NET 11 ships", "Link with a script scheme"]);
    }

    [Fact]
    public async Task FetchAsync_ShouldDropUnsafeLinks_WhenArticleUrlIsNotHttp()
    {
        _host.Http.When("*").Respond("application/json", SourceTestHost.Fixture("newsapi-everything.json"));

        var result = await Source().FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        var items = result.ShouldBeOfType<SourceSuccess>().Items;
        items[0].Url.ShouldBe(new Uri("https://example.com/net11"));
        items[0].Summary.ShouldBe("The release brings faster startup.");
        items[1].Url.ShouldBeNull();
    }

    [Fact]
    public void Source_ShouldUseNewsName_WhenItReplacesHackerNews()
    {
        Source().Name.ShouldBe("news");
        new NewsApiOptions().BaseAddress.ShouldBe(new Uri("https://newsapi.org/"));
    }

    private NewsApiSource Source() => new(_host.Fetcher, Options.Create(new NewsApiOptions { ApiKey = ApiKey }), new RankRelevanceScorer());
}
