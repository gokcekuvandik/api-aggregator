using System.Net;
using System.Text.Json;
using Aggregator.Api.IntegrationTests.Infrastructure;
using Aggregator.Application.Statistics;
using Aggregator.Domain.Statistics;

namespace Aggregator.Api.IntegrationTests.Endpoints;

public sealed class SourcesAndStatisticsEndpointTests(AggregatorApiFactory factory) : IClassFixture<AggregatorApiFactory>
{
    [Fact]
    public async Task GetSources_ShouldListRegisteredSources_WhenSourcesAreRegistered()
    {
        using var client = factory.CreateClientWith(
            StubDataSource.InState("news", "Success"),
            StubDataSource.InState("weather", "Success"));

        using var response = await client.GetAsync(ApiRoutes.Sources, TestContext.Current.CancellationToken);

        var items = (await response.ShouldBeJson(HttpStatusCode.OK)).GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("name").GetString()).ShouldBe(["news", "weather"]);
        items[0].GetProperty("categories").EnumerateArray().Select(c => c.GetString()).ShouldBe(["news"]);
    }

    [Fact]
    public async Task GetStatistics_ShouldReturnTotalsBucketsAndPerformancePerApi_WhenStatisticsExist()
    {
        var reader = new StubStatisticsReader(
            Snapshot(new UpstreamApi("news", null), PerformanceBucket.Average),
            Snapshot(new UpstreamApi("news", "hacker-news"), null) with { TotalRequests = 0, CacheHits = 3 });
        using var client = factory.CreateClientWith(reader);

        using var response = await client.GetAsync(ApiRoutes.Statistics, TestContext.Current.CancellationToken);

        var items = (await response.ShouldBeJson(HttpStatusCode.OK)).GetProperty("items");
        var primary = items[0];
        primary.GetProperty("source").GetString().ShouldBe("news");
        primary.GetProperty("provider").ValueKind.ShouldBe(JsonValueKind.Null);
        primary.GetProperty("totalRequests").GetInt64().ShouldBe(4);
        primary.GetProperty("averageMs").GetDouble().ShouldBe(125);
        primary.GetProperty("performance").GetString().ShouldBe("Average");
        var buckets = primary.GetProperty("buckets");
        buckets.GetProperty("fast").GetProperty("count").GetInt64().ShouldBe(1);
        buckets.GetProperty("fast").GetProperty("averageMs").GetDouble().ShouldBe(40);
        buckets.GetProperty("average").GetProperty("count").GetInt64().ShouldBe(2);
        buckets.GetProperty("average").GetProperty("averageMs").GetDouble().ShouldBe(150);
        buckets.GetProperty("slow").GetProperty("count").GetInt64().ShouldBe(1);
        buckets.GetProperty("slow").GetProperty("averageMs").GetDouble().ShouldBe(160);
        primary.GetProperty("retries").GetInt64().ShouldBe(5);
        primary.GetProperty("failures").GetInt64().ShouldBe(1);
        primary.GetProperty("cancelled").GetInt64().ShouldBe(2);
        primary.GetProperty("rejected").GetInt64().ShouldBe(3);
        primary.GetProperty("cacheHits").GetInt64().ShouldBe(2);
        var secondary = items[1];
        secondary.GetProperty("provider").GetString().ShouldBe("hacker-news");
        secondary.GetProperty("performance").ValueKind.ShouldBe(JsonValueKind.Null);
        secondary.GetProperty("cacheHits").GetInt64().ShouldBe(3);
    }

    [Fact]
    public async Task GetStatistics_ShouldReturnEmptyList_WhenNothingWasRecorded()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(ApiRoutes.Statistics, TestContext.Current.CancellationToken);

        (await response.ShouldBeJson(HttpStatusCode.OK)).GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    private static ApiStatisticsResult Snapshot(UpstreamApi api, PerformanceBucket? performance) => new(
        api,
        TotalRequests: 4,
        AverageDuration: TimeSpan.FromMilliseconds(125),
        Performance: performance,
        Fast: new BucketStatistics(1, TimeSpan.FromMilliseconds(40)),
        Average: new BucketStatistics(2, TimeSpan.FromMilliseconds(150)),
        Slow: new BucketStatistics(1, TimeSpan.FromMilliseconds(160)),
        Retries: 5,
        Failures: 1,
        Cancelled: 2,
        Rejected: 3,
        CacheHits: 2,
        Recent: new WindowStatistics(TimeSpan.FromMinutes(5), 4, TimeSpan.FromMilliseconds(125)));

    private sealed class StubStatisticsReader(params ApiStatisticsResult[] snapshots) : IStatisticsReader
    {
        public IReadOnlyList<ApiStatisticsResult> GetSnapshots() => snapshots;
    }
}
