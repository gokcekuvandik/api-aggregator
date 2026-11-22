using System.Net;
using System.Text.Json;
using Aggregator.Api.IntegrationTests.Infrastructure;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Aggregator.Api.IntegrationTests.Sources;

/// <summary>Request statistics end to end: real handlers measure calls to a WireMock upstream and <c>/statistics</c> reports them.</summary>
public sealed class StatisticsTests : IClassFixture<AggregatorApiFactory>, IClassFixture<UpstreamServer>
{
    private const string Repositories = "?sources=repositories&q=dotnet";

    private readonly AggregatorApiFactory _factory;
    private readonly UpstreamServer _upstream;

    public StatisticsTests(AggregatorApiFactory factory, UpstreamServer upstream)
    {
        _factory = factory;
        _upstream = upstream;
        _upstream.Server.Reset();
        _upstream.StubKeylessSources();
    }

    [Fact]
    public async Task GetStatistics_ShouldPutARequestInTheSlowBucket_WhenTheUpstreamTakesLongerThanTwoHundredMilliseconds()
    {
        GivenGitHub().RespondWith(UpstreamServer.JsonResponse("github-search.json").WithDelay(TimeSpan.FromMilliseconds(250)));
        using var client = _factory.CreateClientWithUpstreams(_upstream.Settings());
        await AggregateAsync(client, Repositories);

        var repositories = Single(await StatisticsAsync(client), "repositories", provider: null);

        repositories.GetProperty("totalRequests").GetInt64().ShouldBe(1);
        repositories.GetProperty("averageMs").GetDouble().ShouldBeGreaterThanOrEqualTo(250);
        repositories.GetProperty("performance").GetString().ShouldBe("Slow");
        repositories.GetProperty("buckets").GetProperty("slow").GetProperty("count").GetInt64().ShouldBe(1);
        repositories.GetProperty("failures").GetInt64().ShouldBe(0);
    }

    [Fact]
    public async Task GetStatistics_ShouldCountOneRequestWithItsRetry_WhenTheFirstAttemptFails()
    {
        GivenGitHub().InScenario("retry").WillSetStateTo("recovered").RespondWith(Response.Create().WithStatusCode(503));
        GivenGitHub().InScenario("retry").WhenStateIs("recovered").RespondWith(UpstreamServer.JsonResponse("github-search.json"));
        using var client = _factory.CreateClientWithUpstreams(_upstream.Settings());
        await AggregateAsync(client, Repositories);

        var repositories = Single(await StatisticsAsync(client), "repositories", provider: null);

        _upstream.RequestCount(UpstreamServer.GitHubPath).ShouldBe(2);
        repositories.GetProperty("totalRequests").GetInt64().ShouldBe(1);
        repositories.GetProperty("retries").GetInt64().ShouldBe(1);
        repositories.GetProperty("failures").GetInt64().ShouldBe(0);
    }

    [Fact]
    public async Task GetStatistics_ShouldCountACacheHitWithoutANewRequest_WhenTheSameSearchIsRepeated()
    {
        using var client = _factory.CreateClientWithUpstreams(_upstream.Settings());
        await AggregateAsync(client, Repositories);
        await AggregateAsync(client, Repositories);

        var repositories = Single(await StatisticsAsync(client), "repositories", provider: null);

        repositories.GetProperty("totalRequests").GetInt64().ShouldBe(1);
        repositories.GetProperty("cacheHits").GetInt64().ShouldBe(1);
    }

    [Fact]
    public async Task GetStatistics_ShouldReportThePrimaryFailureAndTheSecondaryApart_WhenTheSourceFallsBack()
    {
        _upstream.Server.Given(Request.Create().WithPath(UpstreamServer.NewsApiPath).UsingGet()).RespondWith(Response.Create().WithStatusCode(503));
        var settings = _upstream.Settings();
        settings["Sources:news:ApiKey"] = "newsapi-test-key";
        using var client = _factory.CreateClientWithUpstreams(settings);
        await AggregateAsync(client, "?sources=news");

        var statistics = await StatisticsAsync(client);

        var primary = Single(statistics, "news", provider: null);
        primary.GetProperty("totalRequests").GetInt64().ShouldBe(1);
        primary.GetProperty("failures").GetInt64().ShouldBe(1);
        primary.GetProperty("retries").GetInt64().ShouldBe(2);
        var secondary = Single(statistics, "news", provider: "hacker-news");
        secondary.GetProperty("totalRequests").GetInt64().ShouldBe(1);
        secondary.GetProperty("failures").GetInt64().ShouldBe(0);
    }

    [Fact]
    public async Task GetStatistics_ShouldCountARevalidatedResponseAsASuccess_WhenTheUpstreamAnswersNotModified()
    {
        _upstream.Server.Given(GitHubRequest().WithHeader("If-None-Match", "\"v1\"")).AtPriority(0).RespondWith(Response.Create().WithStatusCode(304));
        _upstream.StubJson(UpstreamServer.GitHubPath, "github-search.json", UpstreamServer.OverridePriority, ("ETag", "\"v1\""));
        using var client = _factory.CreateClientWithUpstreams(_upstream.SettingsWithoutCache());
        await AggregateAsync(client, Repositories);
        await AggregateAsync(client, Repositories);

        var repositories = Single(await StatisticsAsync(client), "repositories", provider: null);

        _upstream.RequestCount(UpstreamServer.GitHubPath, status: 304).ShouldBe(1);
        repositories.GetProperty("totalRequests").GetInt64().ShouldBe(2);
        repositories.GetProperty("failures").GetInt64().ShouldBe(0);
    }

    private static IRequestBuilder GitHubRequest() => Request.Create().WithPath(UpstreamServer.GitHubPath).UsingGet();

    private IRespondWithAProvider GivenGitHub() =>
        _upstream.Server.Given(GitHubRequest()).AtPriority(UpstreamServer.OverridePriority);

    private static async Task AggregateAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(new Uri(ApiRoutes.Aggregation + query, UriKind.Relative), TestContext.Current.CancellationToken);
        await response.ShouldBeJson(HttpStatusCode.OK);
    }

    private static async Task<List<JsonElement>> StatisticsAsync(HttpClient client)
    {
        using var response = await client.GetAsync(ApiRoutes.Statistics, TestContext.Current.CancellationToken);
        return [.. (await response.ShouldBeJson(HttpStatusCode.OK)).GetProperty("items").EnumerateArray()];
    }

    private static JsonElement Single(List<JsonElement> items, string source, string? provider) =>
        items.Where(item =>
            item.GetProperty("source").GetString() == source
            && (provider == null
                ? item.GetProperty("provider").ValueKind == JsonValueKind.Null
                : item.GetProperty("provider").GetString() == provider))
            .ShouldHaveSingleItem();
}
