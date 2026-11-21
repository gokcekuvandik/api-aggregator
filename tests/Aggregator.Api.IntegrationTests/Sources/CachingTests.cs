using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using Aggregator.Api.IntegrationTests.Infrastructure;
using Aggregator.Application.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

using static Aggregator.Api.IntegrationTests.Infrastructure.AggregationBody;

namespace Aggregator.Api.IntegrationTests.Sources;

/// <summary>Caching end to end: each source's answer is cached per search, and filtering, sorting and paging run after the cache.</summary>
public sealed class CachingTests : IClassFixture<AggregatorApiFactory>, IClassFixture<UpstreamServer>
{
    private readonly AggregatorApiFactory _factory;
    private readonly UpstreamServer _upstream;

    public CachingTests(AggregatorApiFactory factory, UpstreamServer upstream)
    {
        _factory = factory;
        _upstream = upstream;
        _upstream.Server.Reset();
        _upstream.StubKeylessSources();
    }

    [Fact]
    public async Task GetAggregation_ShouldNotCallAnyUpstreamAgain_WhenTheSameQueryIsRepeated()
    {
        using var client = _factory.CreateClientWithUpstreams(_upstream.Settings());
        var first = await GetAsync(client, "?q=dotnet");

        var second = await GetAsync(client, "?q=dotnet");

        UpstreamServer.Upstreams.ShouldAllBe(upstream => _upstream.RequestCount(upstream.Path, null) == 1);
        Statuses(first).Values.ShouldAllBe(status => status == "Success");
        Statuses(second).Values.ShouldAllBe(status => status == "Cached");
        Ids(second).ShouldBe(Ids(first));
        foreach (var source in second.GetProperty("sources").EnumerateArray())
        {
            source.GetProperty("ageSeconds").GetDouble().ShouldBeGreaterThanOrEqualTo(0);
            source.GetProperty("provider").ValueKind.ShouldBe(JsonValueKind.Null);
            source.GetProperty("error").ValueKind.ShouldBe(JsonValueKind.Null);
        }
    }

    [Fact]
    public async Task GetAggregation_ShouldStillBeACacheHit_WhenOnlySortOrPageDiffer()
    {
        using var client = _factory.CreateClientWithUpstreams(_upstream.Settings());
        await GetAsync(client, "?q=dotnet");

        var resorted = await GetAsync(client, "?q=dotnet&sortBy=date&sortOrder=asc&page=2&pageSize=1");

        UpstreamServer.Upstreams.ShouldAllBe(upstream => _upstream.RequestCount(upstream.Path, null) == 1);
        Statuses(resorted).Values.ShouldAllBe(status => status == "Cached");
        resorted.GetProperty("paging").GetProperty("page").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task GetAggregation_ShouldCallTheUpstreamAgain_WhenTheSearchTextDiffers()
    {
        using var client = _factory.CreateClientWithUpstreams(_upstream.Settings());
        await GetAsync(client, "?sources=repositories&q=dotnet");

        var other = await GetAsync(client, "?sources=repositories&q=rust");

        _upstream.RequestCount(UpstreamServer.GitHubPath, null).ShouldBe(2);
        Statuses(other)["repositories"].ShouldBe("Success");
    }

    [Fact]
    public async Task GetAggregation_ShouldReuseTheEntry_WhenOnlyAFieldTheSourceIgnoresDiffers()
    {
        using var client = _factory.CreateClientWithUpstreams(_upstream.Settings());
        await GetAsync(client, "?sources=repositories&q=dotnet&city=Athens");

        var otherCity = await GetAsync(client, "?sources=repositories&q=DotNet&city=Paris");

        _upstream.RequestCount(UpstreamServer.GitHubPath, null).ShouldBe(1);
        Statuses(otherCity)["repositories"].ShouldBe("Cached");
    }

    [Fact]
    public async Task GetAggregation_ShouldKeepTheSecondaryProvider_WhenItsAnswerIsServedFromTheCache()
    {
        _upstream.Server.Given(Request.Create().WithPath(UpstreamServer.NewsApiPath).UsingGet()).RespondWith(Response.Create().WithStatusCode(503));
        var settings = _upstream.Settings();
        settings["Sources:news:ApiKey"] = "newsapi-test-key";
        using var client = _factory.CreateClientWithUpstreams(settings);
        await GetAsync(client, "?sources=news");

        var second = await GetAsync(client, "?sources=news");

        var news = second.GetProperty("sources").EnumerateArray().Single();
        news.GetProperty("status").GetString().ShouldBe("Cached");
        news.GetProperty("provider").GetString().ShouldBe("hacker-news");
        _upstream.RequestCount(UpstreamServer.NewsApiPath, null).ShouldBe(3);
        _upstream.RequestCount(UpstreamServer.HackerNewsPath, null).ShouldBe(1);
    }

    [Fact]
    public async Task GetAggregationStream_ShouldReportCachedSources_WhenTheAggregationWasJustServed()
    {
        using var client = _factory.CreateClientWithUpstreams(_upstream.Settings());
        await GetAsync(client, string.Empty);

        using var response = await client.GetAsync(ApiRoutes.AggregationStream, TestContext.Current.CancellationToken);
        var stream = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        stream.Split("\"status\":\"Cached\"").Length.ShouldBe(4);
        UpstreamServer.Upstreams.ShouldAllBe(upstream => _upstream.RequestCount(upstream.Path, null) == 1);
    }

    [Fact]
    public async Task GetAggregation_ShouldCountCacheLookupsBySourceAndResult_WhenTheSameQueryIsRepeated()
    {
        var (client, services) = _factory.CreateWithUpstreams(_upstream.Settings());
        using (client)
        {
            using var lookups = new MetricCollector<long>(services.GetRequiredService<IMeterFactory>(), SourceCacheMetrics.MeterName, SourceCacheMetrics.LookupsName);

            await GetAsync(client, "?sources=repositories");
            await GetAsync(client, "?sources=repositories");

            lookups.GetMeasurementSnapshot()
                .Select(m => $"{m.Tags["source"]}:{m.Tags["result"]}")
                .ShouldBe(["repositories:miss", "repositories:hit"]);
        }
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(new Uri(ApiRoutes.Aggregation + query, UriKind.Relative), TestContext.Current.CancellationToken);
        return await response.ShouldBeJson(HttpStatusCode.OK);
    }
}
