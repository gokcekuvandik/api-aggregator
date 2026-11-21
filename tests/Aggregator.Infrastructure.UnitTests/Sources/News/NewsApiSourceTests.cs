using Aggregator.Application.Sources;
using Aggregator.Infrastructure.Sources.News.NewsApi;
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

    private NewsApiSource Source() => new(_host.Fetcher, Options.Create(new NewsApiOptions { ApiKey = ApiKey }));
}
